// SPDX-License-Identifier: MIT
// VK_LAYER_SCANLINE_overlay: phase 1 prototype.
//
// What this does today:
//   * Chains correctly through the Vulkan loader (instance + device level).
//   * Hooks vkCreateSwapchainKHR / vkDestroySwapchainKHR and records the swapchain
//     extent, format, present mode and image count.
//   * Hooks vkQueuePresentKHR and measures present intervals per swapchain.
//   * Writes a periodic log line and an optional JSON stats file the frontend can read.
//
// What it deliberately does NOT do yet (phase 5): read back or modify swapchain
// images, run FSR passes, generate frames or draw an overlay.

#include <vulkan/vk_layer.h>
#include <vulkan/vulkan.h>

#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

#include "config.hpp"
#include "frame_stats.hpp"
#include "log.hpp"

#if defined(__GNUC__)
#define EDK_EXPORT extern "C" __attribute__((visibility("default")))
#else
#define EDK_EXPORT extern "C"
#endif

namespace scanline {
namespace {

using DispatchKey = void*;

template <typename T>
DispatchKey GetKey(T handle) {
    // Every dispatchable handle starts with a pointer to the loader's dispatch table.
    return *reinterpret_cast<DispatchKey*>(handle);
}

struct InstanceData {
    VkInstance instance{};
    PFN_vkGetInstanceProcAddr gipa{};
    PFN_vkDestroyInstance DestroyInstance{};
    PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR GetPhysicalDeviceSurfaceCapabilitiesKHR{};
    PFN_vkGetPhysicalDeviceProperties GetPhysicalDeviceProperties{};
};

struct SwapchainData {
    VkExtent2D extent{};
    VkFormat format{};
    VkPresentModeKHR present_mode{};
    uint32_t min_image_count{};
    VkImageUsageFlags usage{};
    FrameStats stats;
};

struct DeviceData {
    VkDevice device{};
    VkPhysicalDevice physical_device{};
    InstanceData* instance{};
    std::string device_name;
    PFN_vkGetDeviceProcAddr gdpa{};
    PFN_vkDestroyDevice DestroyDevice{};
    PFN_vkCreateSwapchainKHR CreateSwapchainKHR{};
    PFN_vkDestroySwapchainKHR DestroySwapchainKHR{};
    PFN_vkQueuePresentKHR QueuePresentKHR{};
    std::unordered_map<VkSwapchainKHR, std::unique_ptr<SwapchainData>> swapchains;
};

std::mutex g_lock;
std::unordered_map<DispatchKey, std::unique_ptr<InstanceData>> g_instances;
std::unordered_map<DispatchKey, std::unique_ptr<DeviceData>> g_devices;
// Physical devices share the instance dispatch key, so instance lookup works for them too.

InstanceData* FindInstance(DispatchKey key) {
    auto it = g_instances.find(key);
    return it == g_instances.end() ? nullptr : it->second.get();
}

DeviceData* FindDevice(DispatchKey key) {
    auto it = g_devices.find(key);
    return it == g_devices.end() ? nullptr : it->second.get();
}

template <typename CI, VkLayerFunction FN>
auto* FindLayerLink(const CI* create_info, VkStructureType stype) {
    using Link = std::conditional_t<std::is_same_v<CI, VkInstanceCreateInfo>, VkLayerInstanceCreateInfo,
                                    VkLayerDeviceCreateInfo>;
    auto* p = reinterpret_cast<const Link*>(create_info->pNext);
    while (p && !(p->sType == stype && p->function == FN)) {
        p = reinterpret_cast<const Link*>(p->pNext);
    }
    return const_cast<Link*>(p);
}

// ---------------------------------------------------------------------------
// Instance
// ---------------------------------------------------------------------------

VKAPI_ATTR VkResult VKAPI_CALL CreateInstance(const VkInstanceCreateInfo* pCreateInfo,
                                              const VkAllocationCallbacks* pAllocator,
                                              VkInstance* pInstance) {
    auto* link = FindLayerLink<VkInstanceCreateInfo, VK_LAYER_LINK_INFO>(
        pCreateInfo, VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO);
    if (!link || !link->u.pLayerInfo) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    PFN_vkGetInstanceProcAddr next_gipa = link->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    link->u.pLayerInfo = link->u.pLayerInfo->pNext;  // advance for the next layer

    auto next_create =
        reinterpret_cast<PFN_vkCreateInstance>(next_gipa(VK_NULL_HANDLE, "vkCreateInstance"));
    if (!next_create) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    VkResult res = next_create(pCreateInfo, pAllocator, pInstance);
    if (res != VK_SUCCESS) {
        return res;
    }

    auto data = std::make_unique<InstanceData>();
    data->instance = *pInstance;
    data->gipa = next_gipa;
#define LOAD(name) data->name = reinterpret_cast<PFN_vk##name>(next_gipa(*pInstance, "vk" #name))
    LOAD(DestroyInstance);
    LOAD(GetPhysicalDeviceSurfaceCapabilitiesKHR);
    LOAD(GetPhysicalDeviceProperties);
#undef LOAD

    const char* app = pCreateInfo->pApplicationInfo && pCreateInfo->pApplicationInfo->pApplicationName
                          ? pCreateInfo->pApplicationInfo->pApplicationName
                          : "(null)";
    const char* engine = pCreateInfo->pApplicationInfo && pCreateInfo->pApplicationInfo->pEngineName
                             ? pCreateInfo->pApplicationInfo->pEngineName
                             : "(null)";
    Log("vkCreateInstance ok: process=%s app=%s engine=%s", Config::Get().process_name.c_str(), app,
        engine);

    std::lock_guard lk(g_lock);
    g_instances[GetKey(*pInstance)] = std::move(data);
    return VK_SUCCESS;
}

VKAPI_ATTR void VKAPI_CALL DestroyInstance(VkInstance instance, const VkAllocationCallbacks* pAllocator) {
    std::unique_ptr<InstanceData> data;
    {
        std::lock_guard lk(g_lock);
        auto it = g_instances.find(GetKey(instance));
        if (it == g_instances.end()) {
            return;
        }
        data = std::move(it->second);
        g_instances.erase(it);
    }
    data->DestroyInstance(instance, pAllocator);
}

// ---------------------------------------------------------------------------
// Device
// ---------------------------------------------------------------------------

VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physicalDevice,
                                            const VkDeviceCreateInfo* pCreateInfo,
                                            const VkAllocationCallbacks* pAllocator, VkDevice* pDevice) {
    auto* link = FindLayerLink<VkDeviceCreateInfo, VK_LAYER_LINK_INFO>(
        pCreateInfo, VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO);
    if (!link || !link->u.pLayerInfo) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    PFN_vkGetInstanceProcAddr next_gipa = link->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    PFN_vkGetDeviceProcAddr next_gdpa = link->u.pLayerInfo->pfnNextGetDeviceProcAddr;
    link->u.pLayerInfo = link->u.pLayerInfo->pNext;

    InstanceData* inst = nullptr;
    {
        std::lock_guard lk(g_lock);
        inst = FindInstance(GetKey(physicalDevice));
    }
    auto next_create = reinterpret_cast<PFN_vkCreateDevice>(
        next_gipa(inst ? inst->instance : VK_NULL_HANDLE, "vkCreateDevice"));
    if (!next_create) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    VkResult res = next_create(physicalDevice, pCreateInfo, pAllocator, pDevice);
    if (res != VK_SUCCESS) {
        return res;
    }

    auto data = std::make_unique<DeviceData>();
    data->device = *pDevice;
    data->physical_device = physicalDevice;
    data->instance = inst;
    data->gdpa = next_gdpa;
#define LOAD(name) data->name = reinterpret_cast<PFN_vk##name>(next_gdpa(*pDevice, "vk" #name))
    LOAD(DestroyDevice);
    LOAD(CreateSwapchainKHR);
    LOAD(DestroySwapchainKHR);
    LOAD(QueuePresentKHR);
#undef LOAD

    if (inst && inst->GetPhysicalDeviceProperties) {
        VkPhysicalDeviceProperties props{};
        inst->GetPhysicalDeviceProperties(physicalDevice, &props);
        data->device_name = props.deviceName;
    }
    bool has_swapchain = false;
    for (uint32_t i = 0; i < pCreateInfo->enabledExtensionCount; ++i) {
        if (std::strcmp(pCreateInfo->ppEnabledExtensionNames[i], VK_KHR_SWAPCHAIN_EXTENSION_NAME) == 0) {
            has_swapchain = true;
        }
    }
    Log("vkCreateDevice ok: gpu=\"%s\" VK_KHR_swapchain=%s", data->device_name.c_str(),
        has_swapchain ? "yes" : "no");

    std::lock_guard lk(g_lock);
    g_devices[GetKey(*pDevice)] = std::move(data);
    return VK_SUCCESS;
}

VKAPI_ATTR void VKAPI_CALL DestroyDevice(VkDevice device, const VkAllocationCallbacks* pAllocator) {
    std::unique_ptr<DeviceData> data;
    {
        std::lock_guard lk(g_lock);
        auto it = g_devices.find(GetKey(device));
        if (it == g_devices.end()) {
            return;
        }
        data = std::move(it->second);
        g_devices.erase(it);
    }
    data->DestroyDevice(device, pAllocator);
}

// ---------------------------------------------------------------------------
// Swapchain + present
// ---------------------------------------------------------------------------

const char* PresentModeName(VkPresentModeKHR m) {
    switch (m) {
    case VK_PRESENT_MODE_IMMEDIATE_KHR: return "IMMEDIATE";
    case VK_PRESENT_MODE_MAILBOX_KHR: return "MAILBOX";
    case VK_PRESENT_MODE_FIFO_KHR: return "FIFO";
    case VK_PRESENT_MODE_FIFO_RELAXED_KHR: return "FIFO_RELAXED";
    default: return "OTHER";
    }
}

VKAPI_ATTR VkResult VKAPI_CALL CreateSwapchainKHR(VkDevice device, const VkSwapchainCreateInfoKHR* pCreateInfo,
                                                  const VkAllocationCallbacks* pAllocator,
                                                  VkSwapchainKHR* pSwapchain) {
    DeviceData* dev = nullptr;
    {
        std::lock_guard lk(g_lock);
        dev = FindDevice(GetKey(device));
    }
    if (!dev) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }

    VkSwapchainCreateInfoKHR ci = *pCreateInfo;
    // Phase 5 needs to copy from / write to swapchain images. Request the transfer bits now
    // (only when the surface supports them) so we can verify on real compositors early that
    // adding them does not break swapchain creation. Opt-out: SCANLINE_LAYER_NO_USAGE_PATCH=1.
    if (Config::Get().patch_swapchain_usage && dev->instance &&
        dev->instance->GetPhysicalDeviceSurfaceCapabilitiesKHR) {
        VkSurfaceCapabilitiesKHR caps{};
        if (dev->instance->GetPhysicalDeviceSurfaceCapabilitiesKHR(dev->physical_device, ci.surface, &caps) ==
            VK_SUCCESS) {
            const VkImageUsageFlags wanted = VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            ci.imageUsage |= (wanted & caps.supportedUsageFlags);
        }
    }

    VkResult res = dev->CreateSwapchainKHR(device, &ci, pAllocator, pSwapchain);
    if (res != VK_SUCCESS && ci.imageUsage != pCreateInfo->imageUsage) {
        Log("vkCreateSwapchainKHR failed (%d) with patched usage, retrying with original usage", res);
        ci.imageUsage = pCreateInfo->imageUsage;
        res = dev->CreateSwapchainKHR(device, &ci, pAllocator, pSwapchain);
    }
    if (res != VK_SUCCESS) {
        Log("vkCreateSwapchainKHR failed: %d", res);
        return res;
    }

    auto sc = std::make_unique<SwapchainData>();
    sc->extent = ci.imageExtent;
    sc->format = ci.imageFormat;
    sc->present_mode = ci.presentMode;
    sc->min_image_count = ci.minImageCount;
    sc->usage = ci.imageUsage;
    Log("vkCreateSwapchainKHR ok: %ux%u format=%d present=%s minImages=%u usage=0x%x (app asked 0x%x)",
        ci.imageExtent.width, ci.imageExtent.height, ci.imageFormat, PresentModeName(ci.presentMode),
        ci.minImageCount, ci.imageUsage, pCreateInfo->imageUsage);

    std::lock_guard lk(g_lock);
    dev->swapchains[*pSwapchain] = std::move(sc);
    return VK_SUCCESS;
}

VKAPI_ATTR void VKAPI_CALL DestroySwapchainKHR(VkDevice device, VkSwapchainKHR swapchain,
                                               const VkAllocationCallbacks* pAllocator) {
    DeviceData* dev = nullptr;
    {
        std::lock_guard lk(g_lock);
        dev = FindDevice(GetKey(device));
        if (dev) {
            auto it = dev->swapchains.find(swapchain);
            if (it != dev->swapchains.end()) {
                it->second->stats.Flush(Config::Get(), it->second->extent.width, it->second->extent.height,
                                        PresentModeName(it->second->present_mode), true);
                dev->swapchains.erase(it);
            }
        }
    }
    if (dev) {
        dev->DestroySwapchainKHR(device, swapchain, pAllocator);
    }
}

VKAPI_ATTR VkResult VKAPI_CALL QueuePresentKHR(VkQueue queue, const VkPresentInfoKHR* pPresentInfo) {
    DeviceData* dev = nullptr;
    {
        std::lock_guard lk(g_lock);
        dev = FindDevice(GetKey(queue));  // queues share their device's dispatch key
        if (dev) {
            const auto now = FrameStats::Clock::now();
            for (uint32_t i = 0; i < pPresentInfo->swapchainCount; ++i) {
                auto it = dev->swapchains.find(pPresentInfo->pSwapchains[i]);
                if (it == dev->swapchains.end()) {
                    continue;
                }
                SwapchainData& sc = *it->second;
                sc.stats.OnPresent(now, pPresentInfo->pImageIndices[i]);
                sc.stats.MaybeReport(Config::Get(), sc.extent.width, sc.extent.height,
                                     PresentModeName(sc.present_mode));
            }
        }
    }
    if (!dev) {
        return VK_ERROR_DEVICE_LOST;
    }
    // Phase 5 inserts its passes here, before handing the frame to the next layer/driver.
    return dev->QueuePresentKHR(queue, pPresentInfo);
}

// ---------------------------------------------------------------------------
// Proc address plumbing
// ---------------------------------------------------------------------------

PFN_vkVoidFunction InterceptDevice(const char* name) {
#define HOOK(fn) \
    if (std::strcmp(name, "vk" #fn) == 0) return reinterpret_cast<PFN_vkVoidFunction>(&fn)
    HOOK(DestroyDevice);
    if (Config::Get().disabled) {
        return nullptr;  // kill switch: keep bookkeeping, hook nothing visible
    }
    HOOK(CreateSwapchainKHR);
    HOOK(DestroySwapchainKHR);
    HOOK(QueuePresentKHR);
#undef HOOK
    return nullptr;
}

PFN_vkVoidFunction InterceptInstance(const char* name) {
#define HOOK(fn) \
    if (std::strcmp(name, "vk" #fn) == 0) return reinterpret_cast<PFN_vkVoidFunction>(&fn)
    HOOK(CreateInstance);
    HOOK(DestroyInstance);
    HOOK(CreateDevice);
#undef HOOK
    return InterceptDevice(name);
}

}  // namespace

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetDeviceProcAddr(VkDevice device, const char* pName);

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetInstanceProcAddr(VkInstance instance, const char* pName) {
    if (std::strcmp(pName, "vkGetInstanceProcAddr") == 0) {
        return reinterpret_cast<PFN_vkVoidFunction>(&GetInstanceProcAddr);
    }
    if (std::strcmp(pName, "vkGetDeviceProcAddr") == 0) {
        return reinterpret_cast<PFN_vkVoidFunction>(&GetDeviceProcAddr);
    }
    if (auto fn = InterceptInstance(pName)) {
        return fn;
    }
    if (instance == VK_NULL_HANDLE) {
        return nullptr;
    }
    InstanceData* inst = nullptr;
    {
        std::lock_guard lk(g_lock);
        inst = FindInstance(GetKey(instance));
    }
    return inst ? inst->gipa(instance, pName) : nullptr;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetDeviceProcAddr(VkDevice device, const char* pName) {
    if (std::strcmp(pName, "vkGetDeviceProcAddr") == 0) {
        return reinterpret_cast<PFN_vkVoidFunction>(&GetDeviceProcAddr);
    }
    DeviceData* dev = nullptr;
    {
        std::lock_guard lk(g_lock);
        dev = FindDevice(GetKey(device));
    }
    if (!dev) {
        return nullptr;
    }
    // Only hand out our hook if the next link actually provides the function.
    PFN_vkVoidFunction next = dev->gdpa(device, pName);
    if (next) {
        if (auto fn = InterceptDevice(pName)) {
            return fn;
        }
    }
    return next;
}

}  // namespace scanline

// ---------------------------------------------------------------------------
// Exports (loader-layer interface v2)
// ---------------------------------------------------------------------------

EDK_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL scanline_GetInstanceProcAddr(VkInstance instance,
                                                                                const char* pName) {
    return scanline::GetInstanceProcAddr(instance, pName);
}

EDK_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL scanline_GetDeviceProcAddr(VkDevice device,
                                                                              const char* pName) {
    return scanline::GetDeviceProcAddr(device, pName);
}

EDK_EXPORT VKAPI_ATTR VkResult VKAPI_CALL
vkNegotiateLoaderLayerInterfaceVersion(VkNegotiateLayerInterface* pVersionStruct) {
    if (!pVersionStruct || pVersionStruct->sType != LAYER_NEGOTIATE_INTERFACE_STRUCT) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    if (pVersionStruct->loaderLayerInterfaceVersion > 2) {
        pVersionStruct->loaderLayerInterfaceVersion = 2;
    }
    pVersionStruct->pfnGetInstanceProcAddr = &scanline::GetInstanceProcAddr;
    pVersionStruct->pfnGetDeviceProcAddr = &scanline::GetDeviceProcAddr;
    pVersionStruct->pfnGetPhysicalDeviceProcAddr = nullptr;
    return VK_SUCCESS;
}
