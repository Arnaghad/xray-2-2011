#pragma once

#include <cstdlib>
#include <iostream>

#define XRAY_TEST_CHECK(expr)                                                     \
    do {                                                                          \
        if (!(expr)) {                                                            \
            std::cerr << "FAIL: " << __FILE__ << ":" << __LINE__ << ": " #expr   \
                      << '\n';                                                    \
            std::abort();                                                         \
        }                                                                         \
    } while (false)
