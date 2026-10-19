using System.Text;

namespace Rimlight.Native.Tests;

// The shortcut's text through the C ABI: the Windows parser, so both apps read the setting the same way.
public sealed unsafe class ShellExportsTests
{
    [Fact]
    public void TheDefaultShortcutParsesAndFormatsBack()
    {
        delegate* unmanaged<byte*, int*, int*, int> parse = &Exports.HotkeyParse;
        delegate* unmanaged<int, int, byte*, int, int> format = &Exports.HotkeyFormat;
        byte[] text = Encoding.UTF8.GetBytes("ctrl + alt + l\0");
        int modifiers, key;
        fixed (byte* t = text) Assert.Equal(1, parse(t, &modifiers, &key));
        Assert.Equal(1 | 2, modifiers);
        Assert.Equal('L', key);

        byte[] buffer = new byte[64];
        fixed (byte* b = buffer)
        {
            int n = format(modifiers, key, b, buffer.Length);
            Assert.Equal("Ctrl+Alt+L", Encoding.UTF8.GetString(buffer, 0, n));
            Assert.Equal(NativeStatus.InvalidArgument, format(0, 'L', b, buffer.Length)); // a plain letter isn't allowed
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("L")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+L+K")]
    public void TextThatIsNoShortcutParsesToNothing(string value)
    {
        delegate* unmanaged<byte*, int*, int*, int> parse = &Exports.HotkeyParse;
        byte[] text = Encoding.UTF8.GetBytes(value + "\0");
        int modifiers, key;
        fixed (byte* t = text) Assert.Equal(0, parse(t, &modifiers, &key));
    }
}
