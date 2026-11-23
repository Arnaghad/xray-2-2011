#ifndef NATIVE_EDITOR_LOGIC_H_INCLUDED
#define NATIVE_EDITOR_LOGIC_H_INCLUDED

#include <xray/editor/world/engine.h>

namespace xray {
namespace editor {

class native_editor_logic {
public:
    native_editor_logic(xray::editor::engine& engine);
    ~native_editor_logic();

    void tick();
    void render();

private:
    xray::editor::engine& m_engine;
    // Native subsystems (Physics, Sound, etc.)
};

} // namespace editor
} // namespace xray

#endif // #ifndef NATIVE_EDITOR_LOGIC_H_INCLUDED
