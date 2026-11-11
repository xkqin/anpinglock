#include "PresencePolicy.h"
#include <fstream>
#include <iostream>
#include <string>
#include <stdexcept>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream input(argv[1]);
    if (!input) return 2;
    anpinglock::PresencePolicy policy;
    int count = 0;
    std::string command;
    try {
        while (input >> command) {
            if (command == "reset") { policy = {}; continue; }
            if (command == "session") { int value; input >> value; policy.setLocked(value != 0); continue; }
            if (command == "restore") { int seconds; input >> seconds; policy.restore(seconds < 0 ? std::nullopt : std::optional<int>(seconds)); continue; }
            int connected, expected1, expected2, expected3;
            int64_t elapsed, idle;
            input >> connected >> elapsed >> idle;
            std::optional<bool> connection = connected < 0 ? std::nullopt : std::optional<bool>(connected != 0);
            std::optional<int64_t> idleTime = idle < 0 ? std::nullopt : std::optional<int64_t>(idle);
            if (command == "observe") {
                input >> expected1 >> expected2 >> expected3;
                auto actual = policy.observe(connection, elapsed, idleTime);
                if (actual.armed != (expected1 != 0) || actual.shouldLock != (expected2 != 0) || actual.remaining.value_or(-1) != expected3)
                    throw std::runtime_error("observation mismatch");
            } else if (command == "lock") {
                input >> expected1 >> expected2;
                int calls = 0;
                auto result = policy.tryLock(connection, elapsed, idleTime, [&] { ++calls; return expected1 != 0; });
                int actual = result ? (*result ? 1 : 0) : -1;
                if (actual != expected2 || calls != (expected2 < 0 ? 0 : 1)) throw std::runtime_error("lock request mismatch");
            } else throw std::runtime_error("invalid command");
            if (!input) throw std::runtime_error("invalid vector");
            ++count;
        }
        if (count < 1000) throw std::runtime_error("insufficient vectors");
        std::cout << "PASS " << count << " native policy decisions match the managed reference, including lock callback counts\n";
    } catch (std::exception const& error) {
        std::cerr << "FAIL decision " << count << ": " << error.what() << '\n'; return 1;
    }
}
