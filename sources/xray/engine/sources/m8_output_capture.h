#pragma once
#include <xray/engine/m8_output_protocol.h>
#include <xray/engine/m8_control_protocol.h>
#include <xray/render/vulkan/sources/vulkan_backend.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <filesystem>
#include <fstream>
#include <iostream>

namespace xray::engine::m8_outputs {
inline void protected_window(HWND window) {
    auto root=GetAncestor(window,GA_ROOT);
    BYTE alpha=255;DWORD flags=0;COLORREF color=0;
    if(!IsWindow(window) || GetForegroundWindow()==window || GetForegroundWindow()==root
        || GetActiveWindow()==window || GetActiveWindow()==root || GetFocus()==window || GetCapture()==window
        || (IsWindowVisible(window) && !IsIconic(root))
        || !GetLayeredWindowAttributes(root,&color,&alpha,&flags) || !(flags&LWA_ALPHA) || alpha!=0)
        throw std::runtime_error("Output workflow window acquired input/visibility");
}
inline void write_image(std::filesystem::path const& path,render::vulkan::vulkan_backend::captured_frame const& image) {
    if(std::filesystem::exists(path))throw std::runtime_error("Output workflow capture already exists");
    BITMAPFILEHEADER file={};BITMAPINFOHEADER info={};
    file.bfType=0x4d42;file.bfOffBits=sizeof(file)+sizeof(info);file.bfSize=DWORD(file.bfOffBits+image.bgra.size());
    info.biSize=sizeof(info);info.biWidth=LONG(image.width);info.biHeight=-LONG(image.height);info.biPlanes=1;info.biBitCount=32;
    std::ofstream out(path,std::ios::binary);out.write(reinterpret_cast<char*>(&file),sizeof(file));out.write(reinterpret_cast<char*>(&info),sizeof(info));
    out.write(reinterpret_cast<char const*>(image.bgra.data()),image.bgra.size());out.close();
    if(!out)throw std::runtime_error("Output workflow capture write failed");
}
inline void capture(HWND main,unsigned current_frame,pcstr directory) {
    auto request=reinterpret_cast<uintptr_t>(GetPropA(main,request_property));
    if(!request || request==reinterpret_cast<uintptr_t>(GetPropA(main,ack_property)) || current_frame<frame(request))return;
    if(current_frame!=frame(request) || !name(phase(request)))throw std::runtime_error("Missed/unknown paired output capture");
    auto step=phase(request);auto* backend=dynamic_cast<render::vulkan::vulkan_backend*>(render::rhi::rhi_device::ref().get_backend());
    if(!backend)throw std::runtime_error("Output workflow backend missing");
    auto original=backend->output();auto outputs=backend->outputs();
    auto preview=reinterpret_cast<HWND>(GetPropA(main,m8_control::preview_property));
    std::shared_ptr<render::vulkan::presentation_output> primary,secondary;
    for(auto const& output:outputs) {if(output->hwnd==main)primary=output;if(preview && output->hwnd==preview)secondary=output;}
    bool single=step==shot::preview_closed || step==shot::shutdown_ready;
    if(!primary || outputs.size()!=(single?1u:2u) || (!single && (!secondary || primary==secondary
        || primary->surface==secondary->surface || primary->swapchain==secondary->swapchain)))
        throw std::runtime_error("Paired views do not own independent presentation objects");
    protected_window(main);if(secondary)protected_window(preview);
    if(bool(IsIconic(GetAncestor(main,GA_ROOT)))!=(step==shot::main_minimized)
        || (secondary && bool(IsIconic(preview))!=(step==shot::preview_minimized)))
        throw std::runtime_error("Output workflow did not reach the requested minimize state");
    auto folder=std::filesystem::absolute(directory);std::filesystem::create_directories(folder);
    auto take=[&](auto const& output,char const* view) {
        backend->select_output(output);auto image=backend->capture_frame();RECT rect{};
        if(!GetClientRect(output->hwnd,&rect) || image.width!=unsigned(rect.right) || image.height!=unsigned(rect.bottom))
            throw std::runtime_error("Paired capture extent does not match its HWND");
        write_image(folder/(std::string(name(step))+"."+view+".bmp"),image);
    };
    if(step!=shot::main_minimized)take(primary,"main");
    if(secondary && step!=shot::preview_minimized)take(secondary,"preview");
    backend->select_output(original);
    std::cerr<<"M8 outputs capture: step="<<name(step)<<" frame="<<current_frame<<" main_frames="<<primary->presented_frames
        <<" preview_frames="<<(secondary?secondary->presented_frames:0)<<" outputs="<<outputs.size()<<" render_thread="<<threading::current_thread_id()<<'\n';
    if(!SetPropA(main,ack_property,reinterpret_cast<HANDLE>(request)))throw std::runtime_error("Cannot acknowledge paired output capture");
}
}
