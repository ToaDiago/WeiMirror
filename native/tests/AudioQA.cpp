#include "Audio/WasapiRenderer.h"
#include "Logging.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string_view>
#include <thread>
#include <vector>

using namespace iPhoneMirror::audio;
using namespace iPhoneMirror::audio::detail;

namespace {
int checks{};
void check(bool ok, const char* message) {
    if (!ok) throw std::runtime_error(message);
    ++checks;
    std::cout << "PASS " << message << '\n';
}

// Model actual producer/endpoint timing, including packet bursts and stalls.
// The policy functions are the same functions used by the real WASAPI sink.
void simulate(std::size_t packet, std::size_t startup, std::size_t high_water) {
    constexpr std::size_t capacity = 8192, endpoint = 1056, rate = 48000;
    const auto thresholds = wasapi_queue_thresholds(packet, capacity, endpoint,
        startup, high_water);
    std::size_t queued{}, peak{}, total_drop{};
    bool playing{};
    for (std::size_t ms = 0; ms < 30000; ++ms) {
        const auto before = ms * rate / 1000 / packet;
        const auto after = (ms + 1) * rate / 1000 / packet;
        if (before != after) {
            const auto plan = plan_wasapi_enqueue(queued, packet, capacity, thresholds);
            total_drop += plan.drop_existing_frames;
            queued = plan.final_frames;
            peak = std::max(peak, queued);
        }
        if (!playing && queued >= thresholds.startup_frames) playing = true;
        if (playing) queued -= std::min(queued, rate / 1000);
    }
    check(playing && peak <= thresholds.high_water_frames && total_drop == 0,
        "steady PCM packet cadence plays without drops or growing backlog");
    const auto recovery = plan_wasapi_enqueue(capacity, packet, capacity, thresholds);
    check(recovery.final_frames <= std::max(thresholds.startup_frames, packet) &&
        recovery.drop_existing_frames != 0,
        "a stalled queue catches up to the playback target in one enqueue");
}

void policy_checks() {
    const auto usb = wasapi_queue_thresholds(1024, 8192, 1056);
    check(usb.startup_frames == 1056 && usb.high_water_frames == 2880,
        "48 kHz USB starts below three packets and bounds normal backlog to 60 ms");
    const auto large = wasapi_queue_thresholds(4096, 8192, 1056);
    check(large.startup_frames == 4096 && large.high_water_frames == 5152,
        "4096-frame USB audio starts with one packet rather than waiting for two");
    const auto network = wasapi_queue_thresholds(352, 8192, 1056, 1920, 4800);
    check(network.startup_frames == 1920 && network.high_water_frames == 4800,
        "48 kHz AirPlay uses 40 ms startup and a 100 ms high-water mark");
    const auto catchup = plan_wasapi_enqueue(4700, 352, 8192, network);
    check(catchup.final_frames == 1920 && catchup.drop_existing_frames == 3132,
        "AirPlay bursts recover to 40 ms instead of retaining high-water latency");
    const auto one = plan_wasapi_enqueue(0, 4096, 8192, large);
    check(one.final_frames == 4096 && one.drop_existing_frames == 0,
        "large source packets are retained intact");
    check(wasapi_render_frames(1056, 0, 352) == 352 &&
        wasapi_render_frames(1056, 704, 352) == 352,
        "short PCM packets drain immediately even when they cannot fill an endpoint");
    check(wasapi_render_frames(1056, 0, 0) == 0 &&
        wasapi_render_frames(1056, 1056, 352) == 0 &&
        wasapi_render_frames(1056, 1057, 352) == 0,
        "empty or full endpoints do not request invalid WASAPI buffers");
    check(wasapi_render_frames(1056, 704, 2000) == 352,
        "render requests never exceed available endpoint space");
    const auto empty = wasapi_queue_thresholds(1024, 0, 1056);
    check(empty.startup_frames == 0 &&
        plan_wasapi_enqueue(100, 100, 0, empty).final_frames == 0,
        "empty capacity is handled safely");
    const auto saturated = wasapi_queue_thresholds(16384, 8192, 16384);
    check(saturated.startup_frames == 8192 && saturated.high_water_frames == 8192,
        "oversized threshold arithmetic remains bounded");
    for (auto packet : {352U, 1024U, 2048U, 4096U}) {
        simulate(packet, 960, 2880);
        simulate(packet, 1920, 4800);
    }
    // Different rates and packet sizes must never lose recent incoming PCM
    // or overflow the ring; after a catch-up, the old backlog stays bounded.
    bool valid = true;
    for (auto rate : {8000U, 44100U, 48000U, 96000U, 192000U}) {
        const std::size_t capacity = std::max(8192U, rate / 6);
        for (auto packet : {160U, 352U, 1024U, 2048U, 4096U}) {
            const auto thresholds = wasapi_queue_thresholds(packet, capacity,
                rate / 50, (rate * 40 + 999) / 1000, (rate * 100 + 999) / 1000);
            for (std::size_t queued = 0; queued <= capacity; queued += 31) {
                const auto p = plan_wasapi_enqueue(queued, packet, capacity, thresholds);
                valid &= p.final_frames <= capacity && p.final_frames >= packet &&
                    p.drop_existing_frames <= queued &&
                    p.final_frames == queued - p.drop_existing_frames + packet;
            }
        }
    }
    check(valid, "sample-rate and packet-size sweep preserves queue bounds and PCM accounting");
}

void endpoint_smoke() {
    iPhoneMirror::coremedia::AudioStreamBasicDescription format{
        .sample_rate = 48000, .format_id = 0x6c70636dU, .format_flags = 1U << 2U,
        .bytes_per_packet = 4, .frames_per_packet = 1, .bytes_per_frame = 4,
        .channels_per_frame = 2, .bits_per_channel = 16,
    };
    // Digital silence only; this optional smoke test never plays a test tone.
    WasapiRenderer renderer(format, true, 1.0F, WasapiBufferingMode::NetworkJitter);
    std::vector<std::uint8_t> silence(352 * 4);
    auto deadline = std::chrono::steady_clock::now();
    for (int i = 0; i < 300; ++i) {
        renderer.enqueue(silence);
        deadline += std::chrono::nanoseconds(7333333);
        std::this_thread::sleep_until(deadline);
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(150));
    const auto stats = renderer.stats();
    renderer.stop();
    check(stats.active && stats.rendered_frames > 48000 && stats.queued_frames == 0,
        "real WASAPI endpoint starts, plays PCM and drains the final short packet");
    std::cout << "endpoint rendered=" << stats.rendered_frames << " dropped="
        << stats.dropped_frames << " underruns=" << stats.underruns << '\n';
}
}

int main(int argc, char** argv) {
    try {
        policy_checks();
        if (argc == 2 && std::string_view(argv[1]) == "--endpoint-smoke") endpoint_smoke();
        iPhoneMirror::logging::shutdown();
        std::cout << checks << " audio checks passed\n";
        return 0;
    } catch (const std::exception& error) {
        iPhoneMirror::logging::shutdown();
        std::cerr << "FAIL " << error.what() << '\n';
        return 1;
    }
}
