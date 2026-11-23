using System;

namespace xray.editor_base
{
    public delegate bool BooleanGetter();
    public delegate void BooleanSetter(bool value);

    public delegate int IntegerGetter();
    public delegate void IntegerSetter(int value);

    public delegate float FloatGetter();
    public delegate void FloatSetter(float value);

    public delegate string StringGetter();
    public delegate void StringSetter(string value);
}
