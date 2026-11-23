#ifndef XRAY_EDITOR_MANAGED_HOST_H_INCLUDED
#define XRAY_EDITOR_MANAGED_HOST_H_INCLUDED
#include <xray/editor/world/host_bridge.h>
#include <xray/editor/world/sdk_property_bridge.h>
#include <xray/editor/world/sdk_resource_bridge.h>
#include <xray/editor/world/sdk_scene_bridge.h>
#include <filesystem>
namespace xray::editor::hosting {
create_world_fn factory(std::filesystem::path directory={});
bind_sdk_fn sdk_factory(std::filesystem::path directory={});
bind_resources_fn resource_factory(std::filesystem::path directory={});
bind_scene_fn scene_factory(std::filesystem::path directory={});
}
#endif
