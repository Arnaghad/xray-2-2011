#include "pch.h"
#include "vulkan_ui_renderer.h"
#include <xray/ui/ui.h>
#include <xray/render/engine/world.h>
#include <cmath>
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
std::vector<ui::vertex> layout_ui_text(pcstr text,xray::ui::font const& font,math::float2 const& position,
    math::color const& color,math::color const& selection,u32 max_width,bool multiline,u32 start,u32 end) {
    if (!text) throw std::runtime_error("UI text is null");
    size_t length=0; while (length<=16384 && text[length]) ++length;
    if (length>16384) throw std::runtime_error("UI text exceeds 16384 byte glyphs");
    float height=font.get_height(),height_uv=font.get_height_ts();
    if (!std::isfinite(position.x) || !std::isfinite(position.y) || !std::isfinite(height) || !std::isfinite(height_uv)
        || height<=0 || height>10000 || height_uv<=0 || height_uv>1 || (multiline && !max_width) || start>end)
        throw std::runtime_error("Invalid UI text layout parameters");
    std::array<float,256> widths={}; std::array<math::float3,256> uvs;
    std::array<bool,256> seen={};
    auto glyph=[&](u8 ch) {
        if (seen[ch]) return;
        auto const& tc=font.get_char_tc(ch); auto uv=font.get_char_tc_ts(ch);
        if (!std::isfinite(tc.z) || tc.z<0 || tc.z>10000 || !std::isfinite(uv.x) || !std::isfinite(uv.y) || !std::isfinite(uv.z)
            || uv.x<0 || uv.y<0 || uv.z<0 || uv.x+uv.z>1.00001f || uv.y+height_uv>1.00001f)
            throw std::runtime_error("Invalid UI font metrics/UVs");
        widths[ch]=tc.z; uvs[ch]=uv; seen[ch]=true;
    };
    for (size_t i=0;i<length;++i) if (text[i]!='\r' && text[i]!='\n' && text[i]!='\t') glyph(u8(text[i]));
    if (std::find(text,text+length,'\t')!=text+length) glyph(' ');
    auto separator=[](char ch) { return ch==' ' || ch=='\t' || ch=='\r' || ch=='\n' || ch==',' || ch=='.' || ch==':' || ch=='!'; };
    std::vector<ui::vertex> vertices; vertices.reserve(length*4);
    float x=0,y=0;
    for (size_t i=0;i<length;++i) {
        u8 ch=u8(text[i]);
        if (ch=='\r' || ch=='\n') {
            if (ch=='\r' && i+1<length && text[i+1]=='\n') ++i;
            x=0; y+=height; continue;
        }
        float width=ch=='\t' ? widths[' ']*4 : widths[ch];
        if (multiline && x>0 && !separator(char(ch)) && (!i || separator(text[i-1]))) {
            float word=0;
            for (size_t j=i;j<length && !separator(text[j]);++j) word+=widths[u8(text[j])];
            if (x+word>max_width) { x=0; y+=height; }
        }
        if (multiline && x>0 && x+width>max_width) { x=0; y+=height; }
        if (ch!='\t' && width>0) {
            auto uv=uvs[ch]; u32 packed=(i>=start && i<end ? selection : color).m_value;
            float left=position.x+x,top=position.y+y,right=left+width,bottom=top+height;
            for (float value : {left,top,right,bottom}) if (!std::isfinite(value) || std::abs(value)>1e6f)
                throw std::runtime_error("UI text positions exceed finite pixel bounds");
            vertices.emplace_back(left,bottom,0,packed,uv.x,uv.y+height_uv);
            vertices.emplace_back(left,top,0,packed,uv.x,uv.y);
            vertices.emplace_back(right,bottom,0,packed,uv.x+uv.z,uv.y+height_uv);
            vertices.emplace_back(right,top,0,packed,uv.x+uv.z,uv.y);
        }
        x+=width;
    }
    return vertices;
}
} } }
void xray::render::engine::world::draw_text(xray::vectora<ui::vertex>& output,pcstr const& text,xray::ui::font const& font,
    xray::float2 const& position,math::color const& color,math::color const& selection,u32 width,bool multiline,u32 start,u32 end) {
    auto vertices=vulkan::layout_ui_text(text,font,position,color,selection,width,multiline,start,end);
    output.assign(vertices.begin(),vertices.end());
}
