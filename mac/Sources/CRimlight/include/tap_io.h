// The system audio tap's IO proc (docs/10-MACOS.md §2): Core Audio calls it on its real-time thread with the tap's
// buffers, and it only mixes them to mono into the ring (audio_ring.h) and notes when sound last arrived.

#ifndef RIMLIGHT_TAP_IO_H
#define RIMLIGHT_TAP_IO_H

#include <CoreAudio/CoreAudio.h>
#include <stdint.h>

#include "audio_ring.h"

#pragma clang assume_nonnull begin

typedef struct RlcTap RlcTap;

/// The IO proc's state: the ring it writes. Destroy only after the IO proc is stopped and destroyed.
RlcTap *_Nullable rlc_tap_create(RlcRing *ring);
void rlc_tap_destroy(RlcTap *tap);

/// The tap's stream format, set before IO starts: 32-bit float samples, interleaved or one buffer per channel. A
/// format that isn't float makes the IO proc write nothing.
void rlc_tap_set_format(RlcTap *tap, int32_t isFloat32);

/// For AudioDeviceCreateIOProcID, with the RlcTap as client data. Declared with AudioDeviceIOProc's own nullability,
/// so Swift passes it as one.
OSStatus rlc_tap_io_proc(AudioObjectID device, const AudioTimeStamp *now, const AudioBufferList *input,
                         const AudioTimeStamp *inputTime, AudioBufferList *output, const AudioTimeStamp *outputTime,
                         void *_Nullable clientData);

/// Seconds since the IO proc last ran, and since it last saw a sample that wasn't exactly zero; a large number if
/// never. Any thread.
double rlc_tap_seconds_since_callback(const RlcTap *tap);
double rlc_tap_seconds_since_sound(const RlcTap *tap);

#pragma clang assume_nonnull end

#endif
