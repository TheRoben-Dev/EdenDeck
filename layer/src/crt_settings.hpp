// SPDX-License-Identifier: MIT
#pragma once

#include <sys/stat.h>

#include <chrono>
#include <cstdlib>
#include <fstream>
#include <mutex>
#include <string>

namespace scanline {

// The in-game CRT filter's look at one level. Same numbers as the app's CRT effect
// (src/EdenDeck.App/Scripts/Ui/Scanlines.cs, levels 1-3), so Subtle/Medium/Strong match.
struct CrtLook {
    float strength = 0;
    float curve = 0;
    float chroma = 0;
    float vignette = 0;

    static CrtLook ForLevel(int level) {
        switch (level) {
        case 1: return {0.7f, 0.02f, 0.6f, 0.15f};
        case 2: return {1.0f, 0.035f, 1.0f, 0.25f};
        case 3: return {1.4f, 0.05f, 1.5f, 0.28f};
        default: return {};
        }
    }
};

struct CrtState {
    int level = 0;      // 0 off, 1 subtle, 2 medium, 3 strong
    bool motion = true; // false = still picture (the app's Animations setting is off)

    bool operator==(const CrtState&) const = default;
};

// Parses "level=N" / "motion=0|1" lines (the control file the app or the quick menu writes).
// Unknown lines are ignored; values out of range keep the previous value.
inline CrtState ParseCrtControl(const std::string& text, CrtState state) {
    size_t pos = 0;
    while (pos <= text.size()) {
        size_t end = text.find('\n', pos);
        if (end == std::string::npos) {
            end = text.size();
        }
        std::string line = text.substr(pos, end - pos);
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) {
            line.pop_back();
        }
        if (line.rfind("level=", 0) == 0 && line.size() == 7 && line[6] >= '0' && line[6] <= '3') {
            state.level = line[6] - '0';
        } else if (line == "motion=0") {
            state.motion = false;
        } else if (line == "motion=1") {
            state.motion = true;
        }
        pos = end + 1;
    }
    return state;
}

// Current CRT setting: starts from SCANLINE_CRT / SCANLINE_CRT_MOTION and, when
// SCANLINE_CRT_CONTROL names a file, follows that file (checked at most twice a second, by mtime),
// so the setting can change while the game runs.
class CrtSettings {
public:
    static CrtSettings& Get() {
        static CrtSettings s;
        return s;
    }

    CrtState Current() {
        std::lock_guard lk(lock_);
        if (!control_.empty()) {
            const auto now = std::chrono::steady_clock::now();
            if (now - last_check_ >= std::chrono::milliseconds(500)) {
                last_check_ = now;
                struct stat st {};
                if (::stat(control_.c_str(), &st) == 0 &&
                    (st.st_mtim.tv_sec != mtime_.tv_sec || st.st_mtim.tv_nsec != mtime_.tv_nsec)) {
                    mtime_ = st.st_mtim;
                    std::ifstream f(control_);
                    std::string text((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
                    state_ = ParseCrtControl(text, state_);
                }
            }
        }
        return state_;
    }

    // True when the filter may ever be needed in this process (setting on, or a control file
    // that could turn it on). False means the layer never touches the frames.
    bool MayBeUsed() const { return state_.level > 0 || !control_.empty(); }

private:
    CrtSettings() {
        const char* lvl = std::getenv("SCANLINE_CRT");
        if (lvl && lvl[0] >= '0' && lvl[0] <= '3' && lvl[1] == 0) {
            state_.level = lvl[0] - '0';
        }
        const char* motion = std::getenv("SCANLINE_CRT_MOTION");
        state_.motion = !(motion && std::string(motion) == "0");
        if (const char* c = std::getenv("SCANLINE_CRT_CONTROL")) {
            control_ = c;
        }
    }

    std::mutex lock_;
    CrtState state_;
    std::string control_;
    std::chrono::steady_clock::time_point last_check_{};
    struct timespec mtime_ {};
};

}  // namespace scanline
