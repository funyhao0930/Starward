using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Starward.Providers;

/// <summary>
/// 从可执行文件中提取内嵌的最大尺寸图标，写出为 .ico 文件。
/// <para/>
/// 直接读取 RT_GROUP_ICON 与 RT_ICON 资源并重新组装文件头，
/// 不需要解码图像，因此不引入任何图像处理依赖，
/// 也能原样保留 Vista 之后以 PNG 存储的 256x256 图标。
/// </summary>
internal static partial class ExecutableIconExtractor
{

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    private static readonly nint RT_ICON = 3;

    private static readonly nint RT_GROUP_ICON = 14;


    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryEx(string lpLibFileName, nint hFile, uint dwFlags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint hModule);

    [LibraryImport("kernel32.dll", EntryPoint = "EnumResourceNamesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumResourceNames(nint hModule, nint lpType, EnumResNameProc lpEnumFunc, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "FindResourceW")]
    private static partial nint FindResource(nint hModule, nint lpName, nint lpType);

    [LibraryImport("kernel32.dll")]
    private static partial nint LoadResource(nint hModule, nint hResInfo);

    [LibraryImport("kernel32.dll")]
    private static partial nint LockResource(nint hResData);

    [LibraryImport("kernel32.dll")]
    private static partial uint SizeofResource(nint hModule, nint hResInfo);


    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumResNameProc(nint hModule, nint lpszType, nint lpszName, nint lParam);



    /// <summary>
    /// 提取最大尺寸的图标并写入指定路径，成功时返回 true
    /// </summary>
    public static bool TryExtract(string executablePath, string destinationIcoPath)
    {
        if (!File.Exists(executablePath))
        {
            return false;
        }
        nint module = LoadLibraryEx(executablePath, 0, LOAD_LIBRARY_AS_DATAFILE);
        if (module == 0)
        {
            return false;
        }
        try
        {
            if (FindFirstGroupIconName(module) is not nint groupName)
            {
                return false;
            }
            byte[]? ico = BuildIcoFromGroup(module, groupName);
            if (ico is null)
            {
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destinationIcoPath)!);
            File.WriteAllBytes(destinationIcoPath, ico);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            FreeLibrary(module);
        }
    }



    /// <summary>
    /// 应用程序的主图标是资源中的第一个图标组
    /// </summary>
    private static nint? FindFirstGroupIconName(nint module)
    {
        nint found = 0;
        bool hasValue = false;
        EnumResourceNames(module, RT_GROUP_ICON, (_, _, name, _) =>
        {
            found = name;
            hasValue = true;
            return false; // 只要第一个
        }, 0);
        return hasValue ? found : null;
    }


    private static byte[]? GetResourceBytes(nint module, nint type, nint name)
    {
        nint info = FindResource(module, name, type);
        if (info == 0)
        {
            return null;
        }
        uint size = SizeofResource(module, info);
        nint data = LoadResource(module, info);
        if (size == 0 || data == 0)
        {
            return null;
        }
        nint ptr = LockResource(data);
        if (ptr == 0)
        {
            return null;
        }
        var bytes = new byte[size];
        Marshal.Copy(ptr, bytes, 0, (int)size);
        return bytes;
    }


    /// <summary>
    /// 把 RT_GROUP_ICON 的目录与各个 RT_ICON 图像重新组装成 .ico 文件。
    /// 两者的结构只差最后一个字段：组目录里是资源 ID，文件里是数据偏移。
    /// </summary>
    private static byte[]? BuildIcoFromGroup(nint module, nint groupName)
    {
        const int groupHeaderSize = 6;
        const int groupEntrySize = 14;
        const int fileEntrySize = 16;

        byte[]? group = GetResourceBytes(module, RT_GROUP_ICON, groupName);
        if (group is null || group.Length < groupHeaderSize)
        {
            return null;
        }
        var span = group.AsSpan();
        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (count <= 0 || group.Length < groupHeaderSize + (count * groupEntrySize))
        {
            return null;
        }

        // 收集每个图像的数据，宽高为 0 表示 256
        var images = new List<(byte[] Entry, byte[] Data)>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = span.Slice(groupHeaderSize + (i * groupEntrySize), groupEntrySize);
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);
            byte[]? data = GetResourceBytes(module, RT_ICON, id);
            if (data is null)
            {
                continue;
            }
            images.Add((entry[..12].ToArray(), data));
        }
        if (images.Count == 0)
        {
            return null;
        }

        int total = groupHeaderSize + (images.Count * fileEntrySize) + images.Sum(x => x.Data.Length);
        var ico = new byte[total];
        var output = ico.AsSpan();

        // ICONDIR
        BinaryPrimitives.WriteUInt16LittleEndian(output[0..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(output[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(output[4..], (ushort)images.Count);

        int entryOffset = groupHeaderSize;
        int dataOffset = groupHeaderSize + (images.Count * fileEntrySize);
        foreach ((byte[] entry, byte[] data) in images)
        {
            entry.CopyTo(output[entryOffset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(output[(entryOffset + 8)..], (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(output[(entryOffset + 12)..], (uint)dataOffset);
            data.CopyTo(output[dataOffset..]);
            entryOffset += fileEntrySize;
            dataOffset += data.Length;
        }
        return ico;
    }

}
