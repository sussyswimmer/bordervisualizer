#include "tap_io.h"

#include <mach/mach_time.h>
#include <stdatomic.h>
#include <stdlib.h>

#pragma clang assume_nonnull begin

static mach_timebase_info_data_t timebase;

struct RlcTap {
    RlcRing *ring;
    _Atomic int32_t isFloat32;
    _Atomic uint64_t lastCallback; // mach_absolute_time
    _Atomic uint64_t lastSound;
};

RlcTap *_Nullable rlc_tap_create(RlcRing *ring) {
    RlcTap *tap = calloc(1, sizeof(RlcTap));
    if (tap == NULL) return NULL;
    if (timebase.denom == 0) mach_timebase_info(&timebase);
    tap->ring = ring;
    atomic_init(&tap->isFloat32, 0);
    atomic_init(&tap->lastCallback, 0);
    atomic_init(&tap->lastSound, 0);
    return tap;
}

void rlc_tap_destroy(RlcTap *tap) {
    free(tap);
}

void rlc_tap_set_format(RlcTap *tap, int32_t isFloat32) {
    atomic_store_explicit(&tap->isFloat32, isFloat32, memory_order_release);
}

void rlc_tap_reset_times(RlcTap *tap) {
    uint64_t now = mach_absolute_time();
    atomic_store_explicit(&tap->lastCallback, now, memory_order_relaxed);
    atomic_store_explicit(&tap->lastSound, now, memory_order_relaxed);
}

OSStatus rlc_tap_io_proc(AudioObjectID device, const AudioTimeStamp *now, const AudioBufferList *input,
                         const AudioTimeStamp *inputTime, AudioBufferList *output, const AudioTimeStamp *outputTime,
                         void *_Nullable clientData) {
    (void)device;
    (void)now;
    (void)inputTime;
    (void)output;
    (void)outputTime;
    RlcTap *tap = clientData;
    if (tap == NULL) return noErr;
    uint64_t time = mach_absolute_time();
    atomic_store_explicit(&tap->lastCallback, time, memory_order_relaxed);
    if (input->mNumberBuffers == 0) return noErr;
    if (atomic_load_explicit(&tap->isFloat32, memory_order_acquire) == 0) return noErr;

    // Interleaved: one buffer with every channel. Planar: one buffer per channel. Up to 16 planes.
    enum { MaxPlanes = 16 };
    const float *planes[MaxPlanes];
    UInt32 count = input->mNumberBuffers < MaxPlanes ? input->mNumberBuffers : MaxPlanes;
    UInt32 channels = input->mBuffers[0].mNumberChannels > 0 ? input->mBuffers[0].mNumberChannels : 1;
    int32_t frames = INT32_MAX;
    for (UInt32 b = 0; b < count; b++) {
        planes[b] = input->mBuffers[b].mData;
        if (planes[b] == NULL || input->mBuffers[b].mNumberChannels != channels) return noErr;
        int32_t bufferFrames = (int32_t)(input->mBuffers[b].mDataByteSize / (sizeof(float) * channels));
        if (bufferFrames < frames) frames = bufferFrames; // never read past the shortest buffer
    }
    int sound = 0;
    int32_t samples = frames * (int32_t)channels;
    for (UInt32 b = 0; b < count && !sound; b++)
        for (int32_t i = 0; i < samples && !sound; i++) sound = planes[b][i] != 0.0f;
    if (sound) atomic_store_explicit(&tap->lastSound, time, memory_order_relaxed);
    rlc_ring_write_mono(tap->ring, planes, (int32_t)count, (int32_t)channels, frames);
    return noErr;
}

static double seconds_since(uint64_t then) {
    if (then == 0 || timebase.denom == 0) return 1e9;
    uint64_t elapsed = mach_absolute_time() - then;
    return (double)elapsed * timebase.numer / timebase.denom / 1e9;
}

double rlc_tap_seconds_since_callback(const RlcTap *tap) {
    return seconds_since(atomic_load_explicit((_Atomic uint64_t *)&tap->lastCallback, memory_order_relaxed));
}

double rlc_tap_seconds_since_sound(const RlcTap *tap) {
    return seconds_since(atomic_load_explicit((_Atomic uint64_t *)&tap->lastSound, memory_order_relaxed));
}

#pragma clang assume_nonnull end
