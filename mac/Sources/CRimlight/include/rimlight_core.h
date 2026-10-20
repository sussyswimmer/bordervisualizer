// The C ABI of libRimlightCore: Rimlight.Core and the shared per-frame code, compiled with NativeAOT
// (src/Rimlight.Native/Exports.cs implements it: keep the two in sync, and bump RL_ABI_VERSION on any change).
//
// Strings are UTF-8. Inputs are NUL-terminated. A function that outputs a string writes it into the caller's buffer
// and returns its length in bytes, without the NUL; it writes only when capacity > length, so a caller whose buffer
// was too small calls again with length + 1. Every failure is a negative status. Nothing returned needs freeing.

#ifndef RIMLIGHT_CORE_H
#define RIMLIGHT_CORE_H

#include <stdint.h>

#define RL_ABI_VERSION 1

static const int32_t RL_OK = 0;
static const int32_t RL_INVALID_ARGUMENT = -1; // a null pointer, a negative capacity or a string that isn't UTF-8
static const int32_t RL_IO_ERROR = -2;         // the settings file couldn't be written
static const int32_t RL_BAD_JSON = -3;         // the settings JSON couldn't be read
static const int32_t RL_INTERNAL = -99;        // an unexpected error inside the library

/// The library's ABI version; the app refuses to run against a library whose version isn't RL_ABI_VERSION.
int32_t rl_abi_version(void);

/// RL_ABI_VERSION as this header was compiled with (implemented in CRimlight.c, not in the library).
int32_t rl_header_abi_version(void);

/// The app's display name (Rimlight.Core's AppInfo.Name).
int32_t rl_app_name(char *buffer, int32_t capacity);

/// The version, from <Version> in src/Directory.Build.props.
int32_t rl_version(char *buffer, int32_t capacity);

/// The settings in directory/settings.json, validated, as JSON with the file's field names. A missing or bad file
/// gives the defaults (and a bad one is backed up as settings.bad.json). Never writes settings.json.
/// Returns the JSON's length, RL_INVALID_ARGUMENT or RL_INTERNAL.
int32_t rl_settings_load(const char *directory, char *buffer, int32_t capacity);

/// Validates the settings in json (the format rl_settings_load returns) and writes directory/settings.json
/// atomically, creating the directory. Fields left out keep their defaults. Returns RL_OK, RL_BAD_JSON, RL_IO_ERROR,
/// RL_INVALID_ARGUMENT or RL_INTERNAL.
int32_t rl_settings_save(const char *directory, const char *json);

#endif
