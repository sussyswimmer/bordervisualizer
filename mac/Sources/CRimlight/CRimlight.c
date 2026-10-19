#include "rimlight_core.h"

#include <stdatomic.h>
#include <stdlib.h>

int32_t rl_header_abi_version(void) {
    return RL_ABI_VERSION;
}

struct RlcCounter {
    _Atomic int32_t value;
};

RlcCounter *rlc_counter_create(void) {
    RlcCounter *counter = malloc(sizeof(RlcCounter));
    if (counter != NULL) atomic_init(&counter->value, 0);
    return counter;
}

void rlc_counter_destroy(RlcCounter *counter) {
    free(counter);
}

int32_t rlc_counter_add(RlcCounter *counter, int32_t delta) {
    return atomic_fetch_add_explicit(&counter->value, delta, memory_order_acq_rel) + delta;
}

int32_t rlc_counter_load(const RlcCounter *counter) {
    return atomic_load_explicit((_Atomic int32_t *)&counter->value, memory_order_acquire);
}
