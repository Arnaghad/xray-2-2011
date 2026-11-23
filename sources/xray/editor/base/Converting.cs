using System;
using System.Runtime.InteropServices;

namespace xray.editor_base
{
    /// <summary>
    /// Cross-platform string conversion utilities.
    /// Replaces the former C++/CLI converting.h functions that used
    /// pin_ptr, gcnew, safe_cast, System::String^.
    /// </summary>
    public static class Converting
    {
        /// <summary>
        /// Convert a float value to its string representation.
        /// Replaces C++/CLI: convert_to_string(float) which used gcnew System::String.
        /// </summary>
        public static string ConvertToString(float value)
        {
            return value.ToString("G8");
        }

        /// <summary>
        /// Convert a string to a float value.
        /// Replaces C++/CLI: convert_to_single(System::String^) which used pin_ptr + wcstombs_s + atof.
        /// </summary>
        public static float ConvertToSingle(string str)
        {
            if (float.TryParse(str, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
            return 0.0f;
        }

        /// <summary>
        /// Convert an object to a float value.
        /// Replaces C++/CLI: convert_to_single(System::Object^) which used safe_cast.
        /// </summary>
        public static float ConvertToSingle(object obj)
        {
            return ConvertToSingle((string)obj);
        }

        /// <summary>
        /// Convert a managed string to an unmanaged UTF-8 byte pointer.
        /// Replaces C++/CLI: unmanaged_string(System::String^) which used pin_ptr + PtrToStringChars.
        /// Caller must free the returned pointer using Marshal.FreeHGlobal.
        /// </summary>
        public static IntPtr StringToNative(string str)
        {
            return Marshal.StringToHGlobalAnsi(str);
        }

        /// <summary>
        /// Convert an unmanaged string pointer to a managed string.
        /// </summary>
        public static string NativeToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            return Marshal.PtrToStringAnsi(ptr);
        }

        /// <summary>
        /// Free a native string pointer obtained from StringToNative.
        /// </summary>
        public static void FreeNativeString(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }
}
