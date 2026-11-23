#pragma once

#include "json_value.hpp"

namespace xray::maya_cook::detail {

// Validate both wire versions, including v2 import-only curve metadata.
void validate_exchange_header(json_value const& manifest);

} // namespace xray::maya_cook::detail
