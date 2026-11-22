// SPDX-License-Identifier: MIT
#include <cmath>
#include <cstdio>
#include <cstdlib>

#include "frame_stats.hpp"

using namespace scanline;
using namespace std::chrono;

static int g_failures = 0;
#define CHECK(cond)                                                    \
    do {                                                               \
        if (!(cond)) {                                                 \
            std::fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); \
            ++g_failures;                                              \
        }                                                              \
    } while (0)

static bool Near(double a, double b, double eps = 0.01) { return std::fabs(a - b) < eps; }

int main() {
    // Steady 60 Hz
    {
        FrameStats s;
        auto t = FrameStats::Clock::time_point{};
        for (int i = 0; i < 61; ++i) {
            s.OnPresent(t, i % 3);
            t += microseconds(16667);
        }
        auto w = s.Summarise();
        CHECK(w.presents == 60);
        CHECK(Near(w.fps, 60.0, 0.05));
        CHECK(Near(w.avg_ms, 16.667));
        CHECK(Near(w.min_ms, w.max_ms));
        CHECK(s.TotalPresents() == 61);
    }
    // 30 fps content on a 60 Hz FIFO swapchain with one hitch
    {
        FrameStats s;
        auto t = FrameStats::Clock::time_point{};
        for (int i = 0; i < 100; ++i) {
            s.OnPresent(t, 0);
            t += microseconds(i == 50 ? 66667 : 33333);
        }
        auto w = s.Summarise();
        CHECK(w.presents == 99);
        CHECK(Near(w.max_ms, 66.667));
        CHECK(Near(w.min_ms, 33.333));
        CHECK(w.fps > 29.0 && w.fps < 30.0);
    }
    // Empty
    {
        FrameStats s;
        CHECK(s.Summarise().presents == 0);
    }
    if (g_failures == 0) {
        std::puts("frame_stats_test: all passed");
    }
    return g_failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
