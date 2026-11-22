// SPDX-License-Identifier: MIT
#pragma once

#include <cstdlib>
#include <fstream>
#include <string>

namespace scanline {

// Runtime configuration, read once from the environment.
//   SCANLINE_LAYER_LOG=<path>|stderr   where log lines go (default: stderr)
//   SCANLINE_LAYER_STATS=<path>        JSON stats file rewritten about once per report interval
//   SCANLINE_LAYER_REPORT_MS=<ms>      report interval (default 2000)
//   SCANLINE_LAYER_NO_USAGE_PATCH=1    do not add TRANSFER_SRC/DST to swapchain usage
//   DISABLE_SCANLINE_LAYER=1           global kill switch. With the implicit manifest the loader
//                                      skips the layer; with the explicit manifest the layer
//                                      loads but hooks nothing (pure pass-through).
struct Config {
    std::string log_path;
    std::string stats_path;
    std::string process_name;
    unsigned report_ms = 2000;
    bool patch_swapchain_usage = true;
    bool disabled = false;

    static const Config& Get() {
        static const Config cfg = Load();
        return cfg;
    }

private:
    static std::string Env(const char* name) {
        const char* v = std::getenv(name);
        return v ? std::string(v) : std::string();
    }

    static Config Load() {
        Config c;
        c.log_path = Env("SCANLINE_LAYER_LOG");
        c.stats_path = Env("SCANLINE_LAYER_STATS");
        if (auto ms = Env("SCANLINE_LAYER_REPORT_MS"); !ms.empty()) {
            const unsigned long v = std::strtoul(ms.c_str(), nullptr, 10);
            if (v >= 100 && v <= 60000) {
                c.report_ms = static_cast<unsigned>(v);
            }
        }
        c.patch_swapchain_usage = Env("SCANLINE_LAYER_NO_USAGE_PATCH") != "1";
        c.disabled = Env("DISABLE_SCANLINE_LAYER") == "1";
        std::ifstream comm("/proc/self/comm");
        std::getline(comm, c.process_name);
        if (c.process_name.empty()) {
            c.process_name = "?";
        }
        return c;
    }
};

}  // namespace scanline
