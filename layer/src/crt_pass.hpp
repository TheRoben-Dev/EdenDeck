// SPDX-License-Identifier: MIT
#pragma once

// The in-game CRT filter: a post-process pass that runs on the frame just before it is presented.
//
// For each present it records, per swapchain image: copy the finished frame into a private image,
// then draw one full-screen triangle back into the swapchain image that samples the copy through
// the CRT shader. The submit waits on the app's present semaphores and signals our own, which the
// present then waits on, so nothing about the app's synchronisation changes.
//
// Because this layer sits below lsfg-vk in the layer chain (implicit layers and layers earlier in
// VK_INSTANCE_LAYERS are closer to the app), generated frames come through here too and are
// filtered the same as real ones.
//
// Anything that fails (no transfer-src usage on the swapchain, a present queue without graphics,
// an allocation error) turns the filter off for that swapchain with one log line, and the game
// keeps presenting unfiltered.

#include <vulkan/vk_layer.h>
#include <vulkan/vulkan.h>

#include <chrono>
#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

#include "crt_settings.hpp"
#include "log.hpp"

namespace scanline {

#include "shaders.inc"

struct CrtFns {
    PFN_vkGetSwapchainImagesKHR GetSwapchainImagesKHR{};
    PFN_vkCreateCommandPool CreateCommandPool{};
    PFN_vkDestroyCommandPool DestroyCommandPool{};
    PFN_vkAllocateCommandBuffers AllocateCommandBuffers{};
    PFN_vkFreeCommandBuffers FreeCommandBuffers{};
    PFN_vkBeginCommandBuffer BeginCommandBuffer{};
    PFN_vkEndCommandBuffer EndCommandBuffer{};
    PFN_vkResetCommandBuffer ResetCommandBuffer{};
    PFN_vkCmdPipelineBarrier CmdPipelineBarrier{};
    PFN_vkCmdCopyImage CmdCopyImage{};
    PFN_vkCmdCopyImageToBuffer CmdCopyImageToBuffer{};
    PFN_vkCmdBeginRenderPass CmdBeginRenderPass{};
    PFN_vkCmdEndRenderPass CmdEndRenderPass{};
    PFN_vkCmdBindPipeline CmdBindPipeline{};
    PFN_vkCmdBindDescriptorSets CmdBindDescriptorSets{};
    PFN_vkCmdPushConstants CmdPushConstants{};
    PFN_vkCmdDraw CmdDraw{};
    PFN_vkCmdSetViewport CmdSetViewport{};
    PFN_vkCmdSetScissor CmdSetScissor{};
    PFN_vkQueueSubmit QueueSubmit{};
    PFN_vkCreateFence CreateFence{};
    PFN_vkDestroyFence DestroyFence{};
    PFN_vkWaitForFences WaitForFences{};
    PFN_vkResetFences ResetFences{};
    PFN_vkCreateSemaphore CreateSemaphore{};
    PFN_vkDestroySemaphore DestroySemaphore{};
    PFN_vkCreateImage CreateImage{};
    PFN_vkDestroyImage DestroyImage{};
    PFN_vkGetImageMemoryRequirements GetImageMemoryRequirements{};
    PFN_vkBindImageMemory BindImageMemory{};
    PFN_vkCreateBuffer CreateBuffer{};
    PFN_vkDestroyBuffer DestroyBuffer{};
    PFN_vkGetBufferMemoryRequirements GetBufferMemoryRequirements{};
    PFN_vkBindBufferMemory BindBufferMemory{};
    PFN_vkAllocateMemory AllocateMemory{};
    PFN_vkFreeMemory FreeMemory{};
    PFN_vkMapMemory MapMemory{};
    PFN_vkUnmapMemory UnmapMemory{};
    PFN_vkCreateImageView CreateImageView{};
    PFN_vkDestroyImageView DestroyImageView{};
    PFN_vkCreateSampler CreateSampler{};
    PFN_vkDestroySampler DestroySampler{};
    PFN_vkCreateRenderPass CreateRenderPass{};
    PFN_vkDestroyRenderPass DestroyRenderPass{};
    PFN_vkCreateFramebuffer CreateFramebuffer{};
    PFN_vkDestroyFramebuffer DestroyFramebuffer{};
    PFN_vkCreateShaderModule CreateShaderModule{};
    PFN_vkDestroyShaderModule DestroyShaderModule{};
    PFN_vkCreatePipelineLayout CreatePipelineLayout{};
    PFN_vkDestroyPipelineLayout DestroyPipelineLayout{};
    PFN_vkCreateGraphicsPipelines CreateGraphicsPipelines{};
    PFN_vkDestroyPipeline DestroyPipeline{};
    PFN_vkCreateDescriptorSetLayout CreateDescriptorSetLayout{};
    PFN_vkDestroyDescriptorSetLayout DestroyDescriptorSetLayout{};
    PFN_vkCreateDescriptorPool CreateDescriptorPool{};
    PFN_vkDestroyDescriptorPool DestroyDescriptorPool{};
    PFN_vkAllocateDescriptorSets AllocateDescriptorSets{};
    PFN_vkUpdateDescriptorSets UpdateDescriptorSets{};
    // Instance level
    PFN_vkGetPhysicalDeviceMemoryProperties GetPhysicalDeviceMemoryProperties{};
    PFN_vkGetPhysicalDeviceFormatProperties GetPhysicalDeviceFormatProperties{};

    bool Load(VkDevice device, PFN_vkGetDeviceProcAddr gdpa, VkInstance instance, PFN_vkGetInstanceProcAddr gipa) {
        bool ok = true;
#define D(name) ok &= (name = reinterpret_cast<PFN_vk##name>(gdpa(device, "vk" #name))) != nullptr
        D(GetSwapchainImagesKHR); D(CreateCommandPool); D(DestroyCommandPool); D(AllocateCommandBuffers);
        D(FreeCommandBuffers); D(BeginCommandBuffer); D(EndCommandBuffer); D(ResetCommandBuffer);
        D(CmdPipelineBarrier); D(CmdCopyImage); D(CmdCopyImageToBuffer); D(CmdBeginRenderPass);
        D(CmdEndRenderPass); D(CmdBindPipeline); D(CmdBindDescriptorSets); D(CmdPushConstants); D(CmdDraw);
        D(CmdSetViewport); D(CmdSetScissor); D(QueueSubmit); D(CreateFence); D(DestroyFence); D(WaitForFences);
        D(ResetFences); D(CreateSemaphore); D(DestroySemaphore); D(CreateImage); D(DestroyImage);
        D(GetImageMemoryRequirements); D(BindImageMemory); D(CreateBuffer); D(DestroyBuffer);
        D(GetBufferMemoryRequirements); D(BindBufferMemory); D(AllocateMemory); D(FreeMemory); D(MapMemory);
        D(UnmapMemory); D(CreateImageView); D(DestroyImageView); D(CreateSampler); D(DestroySampler);
        D(CreateRenderPass); D(DestroyRenderPass); D(CreateFramebuffer); D(DestroyFramebuffer);
        D(CreateShaderModule); D(DestroyShaderModule); D(CreatePipelineLayout); D(DestroyPipelineLayout);
        D(CreateGraphicsPipelines); D(DestroyPipeline); D(CreateDescriptorSetLayout);
        D(DestroyDescriptorSetLayout); D(CreateDescriptorPool); D(DestroyDescriptorPool);
        D(AllocateDescriptorSets); D(UpdateDescriptorSets);
#undef D
#define I(name) ok &= (name = reinterpret_cast<PFN_vk##name>(gipa(instance, "vk" #name))) != nullptr
        I(GetPhysicalDeviceMemoryProperties); I(GetPhysicalDeviceFormatProperties);
#undef I
        return ok;
    }
};

// The UNORM twin of an sRGB format (same bits, no decode on read), or the format itself.
inline VkFormat UnormOf(VkFormat f) {
    switch (f) {
    case VK_FORMAT_B8G8R8A8_SRGB: return VK_FORMAT_B8G8R8A8_UNORM;
    case VK_FORMAT_R8G8B8A8_SRGB: return VK_FORMAT_R8G8B8A8_UNORM;
    case VK_FORMAT_A8B8G8R8_SRGB_PACK32: return VK_FORMAT_A8B8G8R8_UNORM_PACK32;
    default: return f;
    }
}

struct CrtPush {
    float width, height, t, motion, strength, curve, chroma, vignette, srgb;
};

// Everything the pass needs for one swapchain.
class CrtSwapchain {
public:
    struct Frame {
        VkImage image{};        // swapchain image (not ours)
        VkImageView view{};     // render target view of it
        VkFramebuffer fb{};
        VkImage copy{};         // private copy of the frame, sampled by the shader
        VkDeviceMemory copy_mem{};
        VkImageView copy_view{};
        VkDescriptorSet set{};
        VkCommandBuffer cmd{};
        VkFence fence{};
        VkSemaphore done{};
        bool submitted = false;
    };

    // Device-wide objects the swapchain pass borrows.
    struct Shared {
        VkDevice device{};
        VkPhysicalDevice physical{};
        const CrtFns* fn{};
        PFN_vkSetDeviceLoaderData set_loader_data{};
    };

    ~CrtSwapchain() { Destroy(); }

    // Builds everything. Returns false (with a log line) if this swapchain can't be filtered.
    bool Init(const Shared& sh, VkSwapchainKHR swapchain, VkFormat format, VkExtent2D extent, VkImageUsageFlags usage,
              uint32_t queue_family) {
        sh_ = sh;
        format_ = format;
        extent_ = extent;
        family_ = queue_family;
        const CrtFns& f = *sh.fn;
        if (!(usage & VK_IMAGE_USAGE_TRANSFER_SRC_BIT) || !(usage & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT)) {
            Log("crt: swapchain images can't be read back (usage=0x%x), filter off", usage);
            return false;
        }
        VkFormatProperties fp{};
        copy_format_ = UnormOf(format);
        f.GetPhysicalDeviceFormatProperties(sh.physical, copy_format_, &fp);
        const VkFormatFeatureFlags need = VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT | VK_FORMAT_FEATURE_TRANSFER_DST_BIT;
        if ((fp.optimalTilingFeatures & need) != need) {
            Log("crt: format %d can't be sampled, filter off", copy_format_);
            return false;
        }
        const bool linear = fp.optimalTilingFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT;
        f.GetPhysicalDeviceMemoryProperties(sh.physical, &mem_props_);

        uint32_t count = 0;
        if (f.GetSwapchainImagesKHR(sh.device, swapchain, &count, nullptr) != VK_SUCCESS || count == 0) {
            return Fail("vkGetSwapchainImagesKHR");
        }
        std::vector<VkImage> images(count);
        if (f.GetSwapchainImagesKHR(sh.device, swapchain, &count, images.data()) != VK_SUCCESS) {
            return Fail("vkGetSwapchainImagesKHR");
        }

        VkCommandPoolCreateInfo pci{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        pci.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        pci.queueFamilyIndex = queue_family;
        if (f.CreateCommandPool(sh.device, &pci, nullptr, &pool_) != VK_SUCCESS) return Fail("command pool");

        VkSamplerCreateInfo sci{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};
        sci.magFilter = sci.minFilter = linear ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
        sci.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        sci.addressModeU = sci.addressModeV = sci.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        sci.maxLod = 0;
        if (f.CreateSampler(sh.device, &sci, nullptr, &sampler_) != VK_SUCCESS) return Fail("sampler");

        VkDescriptorSetLayoutBinding b{};
        b.binding = 0;
        b.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        b.descriptorCount = 1;
        b.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        b.pImmutableSamplers = &sampler_;
        VkDescriptorSetLayoutCreateInfo dl{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
        dl.bindingCount = 1;
        dl.pBindings = &b;
        if (f.CreateDescriptorSetLayout(sh.device, &dl, nullptr, &set_layout_) != VK_SUCCESS) return Fail("set layout");

        VkPushConstantRange pr{VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(CrtPush)};
        VkPipelineLayoutCreateInfo pl{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};
        pl.setLayoutCount = 1;
        pl.pSetLayouts = &set_layout_;
        pl.pushConstantRangeCount = 1;
        pl.pPushConstantRanges = &pr;
        if (f.CreatePipelineLayout(sh.device, &pl, nullptr, &layout_) != VK_SUCCESS) return Fail("pipeline layout");

        if (!CreateRenderPass() || !CreatePipeline()) return false;

        VkDescriptorPoolSize ps{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, count};
        VkDescriptorPoolCreateInfo dp{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};
        dp.maxSets = count;
        dp.poolSizeCount = 1;
        dp.pPoolSizes = &ps;
        if (f.CreateDescriptorPool(sh.device, &dp, nullptr, &desc_pool_) != VK_SUCCESS) return Fail("descriptor pool");

        frames_.resize(count);
        for (uint32_t i = 0; i < count; ++i) {
            if (!InitFrame(frames_[i], images[i])) return false;
        }
        Log("crt: ready on %ux%u format=%d, %u images, %s filtering", extent.width, extent.height, format, count,
            linear ? "linear" : "nearest");
        return true;
    }

    uint32_t queue_family() const { return family_; }

    // Records and submits the pass for one present. Returns the semaphore the present must wait on
    // instead of the app's, or VK_NULL_HANDLE if nothing was submitted (present unchanged).
    VkSemaphore Run(VkQueue queue, uint32_t index, const VkPresentInfoKHR& present, const CrtState& state,
                    float seconds, const char* dump_path) {
        if (index >= frames_.size()) return VK_NULL_HANDLE;
        const CrtFns& f = *sh_.fn;
        Frame& fr = frames_[index];
        if (fr.submitted) {
            f.WaitForFences(sh_.device, 1, &fr.fence, VK_TRUE, UINT64_MAX);
        }
        f.ResetFences(sh_.device, 1, &fr.fence);
        f.ResetCommandBuffer(fr.cmd, 0);
        VkCommandBufferBeginInfo bi{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
        bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        f.BeginCommandBuffer(fr.cmd, &bi);

        if (state.level > 0) {
            // 1. swapchain image -> transfer src, our copy -> transfer dst
            VkImageMemoryBarrier pre[2]{};
            pre[0] = Barrier(fr.image, VK_ACCESS_MEMORY_WRITE_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
                             VK_ACCESS_TRANSFER_READ_BIT, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
                             VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
            pre[1] = Barrier(fr.copy, 0, VK_ACCESS_TRANSFER_WRITE_BIT, VK_IMAGE_LAYOUT_UNDEFINED,
                             VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);
            f.CmdPipelineBarrier(fr.cmd, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0,
                                 nullptr, 0, nullptr, 2, pre);
            // 2. copy the finished frame
            VkImageCopy region{};
            region.srcSubresource = region.dstSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
            region.extent = {extent_.width, extent_.height, 1};
            f.CmdCopyImage(fr.cmd, fr.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, fr.copy,
                           VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
            // 3. copy -> sampled, swapchain image -> colour attachment
            VkImageMemoryBarrier mid[2]{};
            mid[0] = Barrier(fr.copy, VK_ACCESS_TRANSFER_WRITE_BIT, VK_ACCESS_SHADER_READ_BIT,
                             VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
            mid[1] = Barrier(fr.image, VK_ACCESS_TRANSFER_READ_BIT, VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
                             VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL);
            f.CmdPipelineBarrier(fr.cmd, VK_PIPELINE_STAGE_TRANSFER_BIT,
                                 VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                                 0, 0, nullptr, 0, nullptr, 2, mid);
            // 4. draw the filtered frame back (the render pass ends in PRESENT_SRC)
            VkRenderPassBeginInfo rp{VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO};
            rp.renderPass = render_pass_;
            rp.framebuffer = fr.fb;
            rp.renderArea = {{0, 0}, extent_};
            f.CmdBeginRenderPass(fr.cmd, &rp, VK_SUBPASS_CONTENTS_INLINE);
            VkViewport vp{0, 0, float(extent_.width), float(extent_.height), 0, 1};
            VkRect2D sc{{0, 0}, extent_};
            f.CmdSetViewport(fr.cmd, 0, 1, &vp);
            f.CmdSetScissor(fr.cmd, 0, 1, &sc);
            f.CmdBindPipeline(fr.cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline_);
            f.CmdBindDescriptorSets(fr.cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, layout_, 0, 1, &fr.set, 0, nullptr);
            const CrtLook look = CrtLook::ForLevel(state.level);
            const CrtPush push{float(extent_.width), float(extent_.height), state.motion ? seconds : 0.0f,
                               state.motion ? 1.0f : 0.0f, look.strength, look.curve, look.chroma, look.vignette,
                               copy_format_ != format_ ? 1.0f : 0.0f};
            f.CmdPushConstants(fr.cmd, layout_, VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(push), &push);
            f.CmdDraw(fr.cmd, 3, 1, 0, 0);
            f.CmdEndRenderPass(fr.cmd);
        }

        VkBuffer dump_buf{};
        VkDeviceMemory dump_mem{};
        if (dump_path) {
            RecordDump(fr, &dump_buf, &dump_mem);
        }
        f.EndCommandBuffer(fr.cmd);

        std::vector<VkPipelineStageFlags> stages(present.waitSemaphoreCount, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT);
        VkSubmitInfo si{VK_STRUCTURE_TYPE_SUBMIT_INFO};
        si.waitSemaphoreCount = present.waitSemaphoreCount;
        si.pWaitSemaphores = present.pWaitSemaphores;
        si.pWaitDstStageMask = stages.data();
        si.commandBufferCount = 1;
        si.pCommandBuffers = &fr.cmd;
        si.signalSemaphoreCount = 1;
        si.pSignalSemaphores = &fr.done;
        const VkResult r = f.QueueSubmit(queue, 1, &si, fr.fence);
        if (r != VK_SUCCESS) {
            Log("crt: vkQueueSubmit failed (%d)", r);
            fr.submitted = false;
            DestroyDump(dump_buf, dump_mem);
            return VK_NULL_HANDLE;
        }
        fr.submitted = true;
        if (dump_path) {
            f.WaitForFences(sh_.device, 1, &fr.fence, VK_TRUE, UINT64_MAX);
            WriteDump(dump_mem, dump_path);
            DestroyDump(dump_buf, dump_mem);
        }
        return fr.done;
    }

    void Destroy() {
        if (!sh_.device) return;
        const CrtFns& f = *sh_.fn;
        for (Frame& fr : frames_) {
            if (fr.submitted) f.WaitForFences(sh_.device, 1, &fr.fence, VK_TRUE, UINT64_MAX);
        }
        for (Frame& fr : frames_) {
            if (fr.fence) f.DestroyFence(sh_.device, fr.fence, nullptr);
            if (fr.done) f.DestroySemaphore(sh_.device, fr.done, nullptr);
            if (fr.fb) f.DestroyFramebuffer(sh_.device, fr.fb, nullptr);
            if (fr.view) f.DestroyImageView(sh_.device, fr.view, nullptr);
            if (fr.copy_view) f.DestroyImageView(sh_.device, fr.copy_view, nullptr);
            if (fr.copy) f.DestroyImage(sh_.device, fr.copy, nullptr);
            if (fr.copy_mem) f.FreeMemory(sh_.device, fr.copy_mem, nullptr);
        }
        frames_.clear();
        if (pool_) f.DestroyCommandPool(sh_.device, pool_, nullptr);  // frees the command buffers
        if (desc_pool_) f.DestroyDescriptorPool(sh_.device, desc_pool_, nullptr);
        if (pipeline_) f.DestroyPipeline(sh_.device, pipeline_, nullptr);
        if (render_pass_) f.DestroyRenderPass(sh_.device, render_pass_, nullptr);
        if (layout_) f.DestroyPipelineLayout(sh_.device, layout_, nullptr);
        if (set_layout_) f.DestroyDescriptorSetLayout(sh_.device, set_layout_, nullptr);
        if (sampler_) f.DestroySampler(sh_.device, sampler_, nullptr);
        sh_.device = VK_NULL_HANDLE;
    }

private:
    bool Fail(const char* what) {
        Log("crt: %s failed, filter off for this swapchain", what);
        return false;
    }

    static VkImageMemoryBarrier Barrier(VkImage image, VkAccessFlags src, VkAccessFlags dst, VkImageLayout from,
                                        VkImageLayout to) {
        VkImageMemoryBarrier b{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
        b.srcAccessMask = src;
        b.dstAccessMask = dst;
        b.oldLayout = from;
        b.newLayout = to;
        b.srcQueueFamilyIndex = b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        b.image = image;
        b.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        return b;
    }

    int MemoryType(uint32_t bits, VkMemoryPropertyFlags want) const {
        for (uint32_t i = 0; i < mem_props_.memoryTypeCount; ++i) {
            if ((bits & (1u << i)) && (mem_props_.memoryTypes[i].propertyFlags & want) == want) return int(i);
        }
        return -1;
    }

    bool CreateRenderPass() {
        VkAttachmentDescription a{};
        a.format = format_;
        a.samples = VK_SAMPLE_COUNT_1_BIT;
        a.loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;  // every pixel is written
        a.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        a.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        a.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        a.initialLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        a.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        VkAttachmentReference ref{0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};
        VkSubpassDescription sp{};
        sp.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
        sp.colorAttachmentCount = 1;
        sp.pColorAttachments = &ref;
        VkSubpassDependency dep{};
        dep.srcSubpass = 0;
        dep.dstSubpass = VK_SUBPASS_EXTERNAL;
        dep.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        dep.dstStageMask = VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT;
        dep.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        dep.dstAccessMask = 0;
        VkRenderPassCreateInfo ci{VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO};
        ci.attachmentCount = 1;
        ci.pAttachments = &a;
        ci.subpassCount = 1;
        ci.pSubpasses = &sp;
        ci.dependencyCount = 1;
        ci.pDependencies = &dep;
        return sh_.fn->CreateRenderPass(sh_.device, &ci, nullptr, &render_pass_) == VK_SUCCESS || Fail("render pass");
    }

    bool CreatePipeline() {
        const CrtFns& f = *sh_.fn;
        VkShaderModule vs{}, fs{};
        VkShaderModuleCreateInfo m{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};
        m.codeSize = sizeof(k_fullscreen_vert);
        m.pCode = k_fullscreen_vert;
        if (f.CreateShaderModule(sh_.device, &m, nullptr, &vs) != VK_SUCCESS) return Fail("vertex shader");
        m.codeSize = sizeof(k_crt_frag);
        m.pCode = k_crt_frag;
        if (f.CreateShaderModule(sh_.device, &m, nullptr, &fs) != VK_SUCCESS) {
            f.DestroyShaderModule(sh_.device, vs, nullptr);
            return Fail("fragment shader");
        }
        VkPipelineShaderStageCreateInfo st[2]{};
        st[0] = {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_VERTEX_BIT, vs, "main"};
        st[1] = {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_FRAGMENT_BIT, fs, "main"};
        VkPipelineVertexInputStateCreateInfo vi{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};
        VkPipelineInputAssemblyStateCreateInfo ia{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};
        ia.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
        VkPipelineViewportStateCreateInfo vps{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};
        vps.viewportCount = 1;
        vps.scissorCount = 1;
        VkPipelineRasterizationStateCreateInfo rs{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};
        rs.polygonMode = VK_POLYGON_MODE_FILL;
        rs.cullMode = VK_CULL_MODE_NONE;
        rs.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rs.lineWidth = 1.0f;
        VkPipelineMultisampleStateCreateInfo ms{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};
        ms.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
        VkPipelineColorBlendAttachmentState cba{};
        cba.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT |
                             VK_COLOR_COMPONENT_A_BIT;
        VkPipelineColorBlendStateCreateInfo cb{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};
        cb.attachmentCount = 1;
        cb.pAttachments = &cba;
        const VkDynamicState dyn[] = {VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR};
        VkPipelineDynamicStateCreateInfo ds{VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO};
        ds.dynamicStateCount = 2;
        ds.pDynamicStates = dyn;
        VkGraphicsPipelineCreateInfo ci{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO};
        ci.stageCount = 2;
        ci.pStages = st;
        ci.pVertexInputState = &vi;
        ci.pInputAssemblyState = &ia;
        ci.pViewportState = &vps;
        ci.pRasterizationState = &rs;
        ci.pMultisampleState = &ms;
        ci.pColorBlendState = &cb;
        ci.pDynamicState = &ds;
        ci.layout = layout_;
        ci.renderPass = render_pass_;
        const VkResult r = f.CreateGraphicsPipelines(sh_.device, VK_NULL_HANDLE, 1, &ci, nullptr, &pipeline_);
        f.DestroyShaderModule(sh_.device, vs, nullptr);
        f.DestroyShaderModule(sh_.device, fs, nullptr);
        return r == VK_SUCCESS || Fail("pipeline");
    }

    bool InitFrame(Frame& fr, VkImage image) {
        const CrtFns& f = *sh_.fn;
        fr.image = image;
        VkImageViewCreateInfo vci{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format = format_;
        vci.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        vci.image = image;
        if (f.CreateImageView(sh_.device, &vci, nullptr, &fr.view) != VK_SUCCESS) return Fail("image view");
        VkFramebufferCreateInfo fci{VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO};
        fci.renderPass = render_pass_;
        fci.attachmentCount = 1;
        fci.pAttachments = &fr.view;
        fci.width = extent_.width;
        fci.height = extent_.height;
        fci.layers = 1;
        if (f.CreateFramebuffer(sh_.device, &fci, nullptr, &fr.fb) != VK_SUCCESS) return Fail("framebuffer");

        VkImageCreateInfo ici{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
        ici.imageType = VK_IMAGE_TYPE_2D;
        ici.format = copy_format_;
        ici.extent = {extent_.width, extent_.height, 1};
        ici.mipLevels = 1;
        ici.arrayLayers = 1;
        ici.samples = VK_SAMPLE_COUNT_1_BIT;
        ici.tiling = VK_IMAGE_TILING_OPTIMAL;
        ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
        ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (f.CreateImage(sh_.device, &ici, nullptr, &fr.copy) != VK_SUCCESS) return Fail("copy image");
        VkMemoryRequirements req{};
        f.GetImageMemoryRequirements(sh_.device, fr.copy, &req);
        const int type = MemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (type < 0) return Fail("copy image memory type");
        VkMemoryAllocateInfo mai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        mai.allocationSize = req.size;
        mai.memoryTypeIndex = uint32_t(type);
        if (f.AllocateMemory(sh_.device, &mai, nullptr, &fr.copy_mem) != VK_SUCCESS) return Fail("copy image memory");
        if (f.BindImageMemory(sh_.device, fr.copy, fr.copy_mem, 0) != VK_SUCCESS) return Fail("bind copy image");
        vci.image = fr.copy;
        vci.format = copy_format_;
        if (f.CreateImageView(sh_.device, &vci, nullptr, &fr.copy_view) != VK_SUCCESS) return Fail("copy view");

        VkDescriptorSetAllocateInfo dai{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};
        dai.descriptorPool = desc_pool_;
        dai.descriptorSetCount = 1;
        dai.pSetLayouts = &set_layout_;
        if (f.AllocateDescriptorSets(sh_.device, &dai, &fr.set) != VK_SUCCESS) return Fail("descriptor set");
        VkDescriptorImageInfo dii{VK_NULL_HANDLE, fr.copy_view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
        VkWriteDescriptorSet w{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};
        w.dstSet = fr.set;
        w.descriptorCount = 1;
        w.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        w.pImageInfo = &dii;
        f.UpdateDescriptorSets(sh_.device, 1, &w, 0, nullptr);

        VkCommandBufferAllocateInfo cai{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        cai.commandPool = pool_;
        cai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        cai.commandBufferCount = 1;
        if (f.AllocateCommandBuffers(sh_.device, &cai, &fr.cmd) != VK_SUCCESS) return Fail("command buffer");
        // Command buffers made inside a layer need the loader's dispatch pointer set by hand.
        if (sh_.set_loader_data) {
            sh_.set_loader_data(sh_.device, fr.cmd);
        } else {
            *reinterpret_cast<void**>(fr.cmd) = *reinterpret_cast<void**>(sh_.device);
        }
        VkFenceCreateInfo fe{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
        if (f.CreateFence(sh_.device, &fe, nullptr, &fr.fence) != VK_SUCCESS) return Fail("fence");
        VkSemaphoreCreateInfo se{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};
        if (f.CreateSemaphore(sh_.device, &se, nullptr, &fr.done) != VK_SUCCESS) return Fail("semaphore");
        return true;
    }

    // Test hook (SCANLINE_LAYER_DUMP): reads the presented image back into a host buffer.
    void RecordDump(Frame& fr, VkBuffer* buf, VkDeviceMemory* mem) {
        const CrtFns& f = *sh_.fn;
        const VkDeviceSize size = VkDeviceSize(extent_.width) * extent_.height * 4;
        VkBufferCreateInfo bci{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
        bci.size = size;
        bci.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        if (f.CreateBuffer(sh_.device, &bci, nullptr, buf) != VK_SUCCESS) return;
        VkMemoryRequirements req{};
        f.GetBufferMemoryRequirements(sh_.device, *buf, &req);
        const int type = MemoryType(req.memoryTypeBits,
                                    VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
        VkMemoryAllocateInfo mai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        mai.allocationSize = req.size;
        mai.memoryTypeIndex = uint32_t(type);
        if (type < 0 || f.AllocateMemory(sh_.device, &mai, nullptr, mem) != VK_SUCCESS) {
            f.DestroyBuffer(sh_.device, *buf, nullptr);
            *buf = VK_NULL_HANDLE;
            return;
        }
        f.BindBufferMemory(sh_.device, *buf, *mem, 0);
        VkImageMemoryBarrier b = Barrier(fr.image, VK_ACCESS_MEMORY_WRITE_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
                                         VK_ACCESS_TRANSFER_READ_BIT, VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
                                         VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
        f.CmdPipelineBarrier(fr.cmd, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr,
                             0, nullptr, 1, &b);
        VkBufferImageCopy region{};
        region.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
        region.imageExtent = {extent_.width, extent_.height, 1};
        f.CmdCopyImageToBuffer(fr.cmd, fr.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, *buf, 1, &region);
        b = Barrier(fr.image, VK_ACCESS_TRANSFER_READ_BIT, 0, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    VK_IMAGE_LAYOUT_PRESENT_SRC_KHR);
        f.CmdPipelineBarrier(fr.cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, 0, 0,
                             nullptr, 0, nullptr, 1, &b);
    }

    void WriteDump(VkDeviceMemory mem, const char* path) {
        if (!mem) return;
        const CrtFns& f = *sh_.fn;
        void* p = nullptr;
        if (f.MapMemory(sh_.device, mem, 0, VK_WHOLE_SIZE, 0, &p) != VK_SUCCESS) return;
        const bool bgr = format_ == VK_FORMAT_B8G8R8A8_UNORM || format_ == VK_FORMAT_B8G8R8A8_SRGB;
        if (FILE* out = std::fopen(path, "wb")) {
            std::fprintf(out, "P6\n%u %u\n255\n", extent_.width, extent_.height);
            const auto* px = static_cast<const uint8_t*>(p);
            std::vector<uint8_t> row(size_t(extent_.width) * 3);
            for (uint32_t y = 0; y < extent_.height; ++y) {
                for (uint32_t x = 0; x < extent_.width; ++x) {
                    const uint8_t* s = px + (size_t(y) * extent_.width + x) * 4;
                    row[x * 3 + 0] = bgr ? s[2] : s[0];
                    row[x * 3 + 1] = s[1];
                    row[x * 3 + 2] = bgr ? s[0] : s[2];
                }
                std::fwrite(row.data(), 1, row.size(), out);
            }
            std::fclose(out);
            Log("crt: wrote %s (%ux%u)", path, extent_.width, extent_.height);
        }
        f.UnmapMemory(sh_.device, mem);
    }

    void DestroyDump(VkBuffer buf, VkDeviceMemory mem) {
        if (buf) sh_.fn->DestroyBuffer(sh_.device, buf, nullptr);
        if (mem) sh_.fn->FreeMemory(sh_.device, mem, nullptr);
    }

    Shared sh_{};
    VkFormat format_{};
    VkFormat copy_format_{};
    VkExtent2D extent_{};
    uint32_t family_{};
    VkPhysicalDeviceMemoryProperties mem_props_{};
    VkCommandPool pool_{};
    VkSampler sampler_{};
    VkDescriptorSetLayout set_layout_{};
    VkPipelineLayout layout_{};
    VkRenderPass render_pass_{};
    VkPipeline pipeline_{};
    VkDescriptorPool desc_pool_{};
    std::vector<Frame> frames_;
};

}  // namespace scanline
