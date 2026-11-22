// SPDX-License-Identifier: MIT
#pragma once

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

#include "config.hpp"
#include "log.hpp"

namespace scanline {

// Present-interval statistics for one swapchain. Header-only so it can be unit tested
// without a Vulkan device. Not thread safe on its own; the caller holds the layer lock.
class FrameStats {
public:
    using Clock = std::chrono::steady_clock;

    struct Window {
        uint64_t presents = 0;
        double seconds = 0.0;
        double fps = 0.0;
        double avg_ms = 0.0;
        double min_ms = 0.0;
        double max_ms = 0.0;
        double p99_ms = 0.0;  // 99th percentile frame time ("1% low" as a frame time)
    };

    void OnPresent(Clock::time_point now, uint32_t image_index) {
        if (have_last_) {
            intervals_ms_.push_back(std::chrono::duration<double, std::milli>(now - last_).count());
        } else {
            window_start_ = now;
            first_present_ = now;
        }
        have_last_ = true;
        last_ = now;
        last_image_ = image_index;
        ++total_presents_;
    }

    // Summarise the intervals collected since the last reset.
    Window Summarise() const {
        Window w;
        w.presents = intervals_ms_.size();
        if (intervals_ms_.empty()) {
            return w;
        }
        std::vector<double> sorted = intervals_ms_;
        std::sort(sorted.begin(), sorted.end());
        double sum = 0.0;
        for (double v : sorted) {
            sum += v;
        }
        w.seconds = sum / 1000.0;
        w.avg_ms = sum / static_cast<double>(sorted.size());
        w.fps = w.avg_ms > 0.0 ? 1000.0 / w.avg_ms : 0.0;
        w.min_ms = sorted.front();
        w.max_ms = sorted.back();
        const size_t idx = std::min(sorted.size() - 1, static_cast<size_t>(sorted.size() * 0.99));
        w.p99_ms = sorted[idx];
        return w;
    }

    void MaybeReport(const Config& cfg, uint32_t width, uint32_t height, const char* present_mode) {
        if (!have_last_) {
            return;
        }
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(last_ - window_start_);
        if (elapsed.count() < static_cast<long long>(cfg.report_ms)) {
            return;
        }
        Flush(cfg, width, height, present_mode, false);
    }

    void Flush(const Config& cfg, uint32_t width, uint32_t height, const char* present_mode, bool final_report) {
        const Window w = Summarise();
        if (w.presents > 0) {
            Log("present: %ux%u %s fps=%.1f avg=%.2fms min=%.2fms max=%.2fms p99=%.2fms n=%llu total=%llu%s", width,
                height, present_mode, w.fps, w.avg_ms, w.min_ms, w.max_ms, w.p99_ms,
                static_cast<unsigned long long>(w.presents), static_cast<unsigned long long>(total_presents_),
                final_report ? " (swapchain destroyed)" : "");
            WriteStatsFile(cfg, w, width, height, present_mode);
        }
        intervals_ms_.clear();
        window_start_ = last_;
    }

    uint64_t TotalPresents() const { return total_presents_; }

private:
    void WriteStatsFile(const Config& cfg, const Window& w, uint32_t width, uint32_t height,
                        const char* present_mode) const {
        if (cfg.stats_path.empty()) {
            return;
        }
        const std::string tmp = cfg.stats_path + ".tmp";
        FILE* f = std::fopen(tmp.c_str(), "w");
        if (!f) {
            return;
        }
        std::fprintf(f,
                     "{\"schema\":1,\"process\":\"%s\",\"width\":%u,\"height\":%u,\"present_mode\":\"%s\","
                     "\"fps\":%.2f,\"avg_ms\":%.3f,\"min_ms\":%.3f,\"max_ms\":%.3f,\"p99_ms\":%.3f,"
                     "\"window_presents\":%llu,\"total_presents\":%llu}\n",
                     cfg.process_name.c_str(), width, height, present_mode, w.fps, w.avg_ms, w.min_ms, w.max_ms,
                     w.p99_ms, static_cast<unsigned long long>(w.presents),
                     static_cast<unsigned long long>(total_presents_));
        std::fclose(f);
        std::rename(tmp.c_str(), cfg.stats_path.c_str());
    }

    std::vector<double> intervals_ms_;
    Clock::time_point window_start_{};
    Clock::time_point first_present_{};
    Clock::time_point last_{};
    bool have_last_ = false;
    uint32_t last_image_ = 0;
    uint64_t total_presents_ = 0;
};

}  // namespace scanline
