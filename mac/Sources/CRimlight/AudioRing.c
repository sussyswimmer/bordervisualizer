#include "audio_ring.h"

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

struct RlcRing {
    float *buffer;
    int64_t mask;
    _Atomic int64_t written; // total samples written; producer-owned
    _Atomic int64_t read;    // total samples read; consumer-owned
    _Atomic int64_t dropped; // samples the producer had no room for; producer-owned
};

RlcRing *rlc_ring_create(int32_t capacity) {
    if (capacity < 2 || (capacity & (capacity - 1)) != 0) return NULL;
    RlcRing *ring = calloc(1, sizeof(RlcRing));
    if (ring == NULL) return NULL;
    ring->buffer = calloc((size_t)capacity, sizeof(float));
    if (ring->buffer == NULL) {
        free(ring);
        return NULL;
    }
    ring->mask = capacity - 1;
    atomic_init(&ring->written, 0);
    atomic_init(&ring->read, 0);
    atomic_init(&ring->dropped, 0);
    return ring;
}

void rlc_ring_destroy(RlcRing *ring) {
    if (ring == NULL) return;
    free(ring->buffer);
    free(ring);
}

int32_t rlc_ring_capacity(const RlcRing *ring) {
    return (int32_t)(ring->mask + 1);
}

int32_t rlc_ring_write(RlcRing *ring, const float *samples, int32_t count) {
    if (count <= 0) return 0;
    int64_t capacity = ring->mask + 1;
    int64_t w = atomic_load_explicit(&ring->written, memory_order_relaxed);
    int64_t r = atomic_load_explicit(&ring->read, memory_order_acquire);
    int64_t room = capacity - (w - r);
    int64_t n = count < room ? count : room;
    if (n > 0) {
        int64_t index = w & ring->mask;
        int64_t first = n < capacity - index ? n : capacity - index;
        memcpy(ring->buffer + index, samples, (size_t)first * sizeof(float));
        memcpy(ring->buffer, samples + first, (size_t)(n - first) * sizeof(float));
        atomic_store_explicit(&ring->written, w + n, memory_order_release);
    }
    if (n < count) {
        int64_t d = atomic_load_explicit(&ring->dropped, memory_order_relaxed);
        atomic_store_explicit(&ring->dropped, d + (count - n), memory_order_relaxed);
    }
    return (int32_t)n;
}

int32_t rlc_ring_read(RlcRing *ring, float *destination, int32_t capacity) {
    if (capacity <= 0) return 0;
    int64_t size = ring->mask + 1;
    int64_t r = atomic_load_explicit(&ring->read, memory_order_relaxed);
    int64_t w = atomic_load_explicit(&ring->written, memory_order_acquire);
    int64_t unread = w - r;
    if (unread > capacity) {
        r = w - capacity; // skip the oldest
        unread = capacity;
    }
    int64_t index = r & ring->mask;
    int64_t first = unread < size - index ? unread : size - index;
    memcpy(destination, ring->buffer + index, (size_t)first * sizeof(float));
    memcpy(destination + first, ring->buffer, (size_t)(unread - first) * sizeof(float));
    atomic_store_explicit(&ring->read, r + unread, memory_order_release);
    return (int32_t)unread;
}

int64_t rlc_ring_dropped(const RlcRing *ring) {
    return atomic_load_explicit((_Atomic int64_t *)&ring->dropped, memory_order_relaxed);
}

int32_t rlc_ring_write_mono(RlcRing *ring, const float *const *planes, int32_t planeCount, int32_t channelsPerPlane,
                            int32_t frames) {
    enum { Chunk = 512 };
    float mono[Chunk];
    int32_t channels = planeCount * channelsPerPlane;
    if (planes == NULL || planeCount <= 0 || channelsPerPlane <= 0 || frames <= 0) return 0;
    float scale = 1.0f / (float)channels;
    int32_t total = 0;
    for (int32_t start = 0; start < frames; start += Chunk) {
        int32_t n = frames - start < Chunk ? frames - start : Chunk;
        for (int32_t i = 0; i < n; i++) {
            float sum = 0;
            for (int32_t p = 0; p < planeCount; p++) {
                const float *plane = planes[p] + (int64_t)(start + i) * channelsPerPlane;
                for (int32_t c = 0; c < channelsPerPlane; c++) sum += plane[c];
            }
            mono[i] = sum * scale;
        }
        int32_t written = rlc_ring_write(ring, mono, n);
        total += written;
        if (written < n) break; // full: the rest would be dropped too
    }
    return total;
}
