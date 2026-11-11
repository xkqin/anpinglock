#pragma once
#include <algorithm>
#include <cstdint>
#include <optional>

namespace anpinglock {
using Milliseconds = int64_t;
struct Decision { bool armed{}, shouldLock{}; std::optional<int> remaining; };

// Owned by the worker thread. Session events are synchronized before observe/tryLock.
class PresencePolicy {
    bool armed_{}, locked_{}, issued_{};
    std::optional<Milliseconds> missing_;
public:
    void restore(std::optional<int> seconds) {
        if (locked_) return;
        armed_ = true; issued_ = false;
        missing_ = seconds ? std::optional<Milliseconds>(std::clamp(*seconds, 0, 60) * 1000LL - 60000) : std::nullopt;
    }
    void setLocked(bool value) {
        if (locked_ == value) return;
        locked_ = value; armed_ = false; issued_ = false; missing_.reset();
    }
    Decision observe(std::optional<bool> connected, Milliseconds elapsed, std::optional<Milliseconds> idle) {
        if (locked_) return {};
        if (connected == true) { armed_ = true; issued_ = false; missing_.reset(); return {true, false, {}}; }
        if (!armed_ || issued_) return {armed_, false, {}};
        if (!connected || !idle || *idle < 0) { missing_.reset(); return {true, false, {}}; }
        if (!missing_) missing_ = elapsed;
        missing_ = std::max(*missing_, elapsed - *idle);
        auto remaining = 60000 - (elapsed - *missing_);
        return {true, remaining <= 0, static_cast<int>(std::max<Milliseconds>(0, (remaining + 999) / 1000))};
    }
    template<class Request>
    std::optional<bool> tryLock(std::optional<bool> connected, Milliseconds elapsed, std::optional<Milliseconds> idle, Request request) {
        if (!observe(connected, elapsed, idle).shouldLock) return {};
        issued_ = true;
        bool success = request();
        if (!success) { issued_ = false; missing_ = elapsed; }
        return success;
    }
};
}
