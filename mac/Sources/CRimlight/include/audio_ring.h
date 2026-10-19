// The audio path's real-time half, in C so the Core Audio IO thread never runs Swift or .NET code (doc 02: the audio
// callback only copies into a ring buffer). A port of src/Rimlight.Platform/Audio/AudioRingBuffer.cs and the
// MonoConverter's averaging.

#ifndef RIMLIGHT_AUDIO_RING_H
#define RIMLIGHT_AUDIO_RING_H

#include <stdint.h>

/// A lock-free single-producer / single-consumer ring of mono samples. The IO proc is the only writer and the render
/// thread the only reader. Each side owns one counter and publishes it with a release store; when the ring is full the
/// newest samples are dropped rather than overwriting unread ones.
typedef struct RlcRing RlcRing;

/// A ring of capacity samples (a power of two, at least 2); NULL if it isn't one or memory is short.
RlcRing *rlc_ring_create(int32_t capacity);
void rlc_ring_destroy(RlcRing *ring);
int32_t rlc_ring_capacity(const RlcRing *ring);

/// Producer only: copies up to count samples in and returns how many fit. Never blocks or allocates.
int32_t rlc_ring_write(RlcRing *ring, const float *samples, int32_t count);

/// Consumer only: copies every unread sample, oldest first, into destination and returns the count. If more are
/// unread than fit, the oldest are skipped, so the newest always arrive. Never blocks or allocates.
int32_t rlc_ring_read(RlcRing *ring, float *destination, int32_t capacity);

/// Samples the producer had no room for. Any thread.
int64_t rlc_ring_dropped(const RlcRing *ring);

/// Producer only: averages frames of channels interleaved samples (or one buffer per channel when planes is set) into
/// mono and writes them, in chunks on the stack. Returns how many mono samples fit.
int32_t rlc_ring_write_mono(RlcRing *ring, const float *const *planes, int32_t planeCount, int32_t channelsPerPlane,
                            int32_t frames);

#endif
