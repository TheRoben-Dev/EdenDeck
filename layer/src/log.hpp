// SPDX-License-Identifier: MIT
#pragma once

#include <cstdarg>
#include <cstdio>
#include <mutex>

#include "config.hpp"

namespace edendeck {

inline void Log(const char* fmt, ...) {
    static std::mutex lock;
    static FILE* out = [] {
        const auto& path = Config::Get().log_path;
        if (path.empty() || path == "stderr") {
            return stderr;
        }
        FILE* f = std::fopen(path.c_str(), "a");
        return f ? f : stderr;
    }();
    std::lock_guard lk(lock);
    std::fputs("[edendeck-layer] ", out);
    va_list args;
    va_start(args, fmt);
    std::vfprintf(out, fmt, args);
    va_end(args);
    std::fputc('\n', out);
    std::fflush(out);
}

}  // namespace edendeck
