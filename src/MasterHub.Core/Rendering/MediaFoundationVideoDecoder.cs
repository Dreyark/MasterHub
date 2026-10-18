using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Pomocniczy dekoder wideo oparty o natywne Windows Media Foundation (IMFSourceReader).
/// Dekoduje pliki .mp4, .avi, .mov, .wmv, .webm, .mkv do strumienia klatek NV12,
/// a następnie szybko przeskalowuje je metodą center-crop do natywnego bufora 72x72 LCD.
/// </summary>
internal static class MediaFoundationVideoDecoder
{
    private const uint MF_VERSION = (2 << 16) | 0x70;
    private const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    private const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00000200;

    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MFVideoFormat_NV12 = new("3231564E-0000-0010-8000-00aa00389b71");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint dwFlags = 0);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszURL, IntPtr pAttributes, out IMFSourceReader ppSourceReader);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    public static (int width, int height) GetVideoDimensions(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return (0, 0);

        int hr = MFStartup(MF_VERSION, 0);
        if (hr != 0) return (0, 0);

        try
        {
            hr = MFCreateSourceReaderFromURL(videoPath, IntPtr.Zero, out var reader);
            if (hr != 0 || reader == null) return (0, 0);

            try
            {
                reader.SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
                uint videoStream = FindVideoStreamIndex(reader);

                int hrCurr = reader.GetCurrentMediaType(videoStream, out var currentMediaType);
                if (hrCurr != 0 || currentMediaType == null) return (0, 0);

                try
                {
                    var frameSizeKey = MF_MT_FRAME_SIZE;
                    currentMediaType.GetUINT64(ref frameSizeKey, out ulong frameSize);
                    int w = (int)(frameSize >> 32);
                    int h = (int)(frameSize & 0xFFFFFFFF);
                    return (w, h);
                }
                finally
                {
                    Marshal.ReleaseComObject(currentMediaType);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }
        catch
        {
            return (0, 0);
        }
        finally
        {
            MFShutdown();
        }
    }

    public static List<byte[]> ExtractVideoFrames(
        string videoPath,
        int maxFrames = 120,
        string fitMode = "Cover",
        int scale = 100,
        int offsetX = 0,
        int offsetY = 0)
    {
        var frames = new List<byte[]>();
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath) || maxFrames <= 0)
            return frames;

        int hr = MFStartup(MF_VERSION, 0);
        if (hr != 0) return frames;

        try
        {
            hr = MFCreateSourceReaderFromURL(videoPath, IntPtr.Zero, out var reader);
            if (hr != 0 || reader == null) return frames;

            try
            {
                reader.SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);
                uint videoStream = FindVideoStreamIndex(reader);

                // Configure uncompressed NV12 output for fast decoding and direct sampling
                MFCreateMediaType(out var mediaType);
                try
                {
                    var majorType = MF_MT_MAJOR_TYPE;
                    var videoType = MFMediaType_Video;
                    var subTypeKey = MF_MT_SUBTYPE;
                    var nv12 = MFVideoFormat_NV12;

                    mediaType.SetGUID(ref majorType, ref videoType);
                    mediaType.SetGUID(ref subTypeKey, ref nv12);

                    int hrSet = reader.SetCurrentMediaType(videoStream, IntPtr.Zero, mediaType);
                    if (hrSet != 0) return frames;
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaType);
                }

                int hrCurr = reader.GetCurrentMediaType(videoStream, out var currentMediaType);
                if (hrCurr != 0 || currentMediaType == null) return frames;

                int width;
                int height;
                try
                {
                    var frameSizeKey = MF_MT_FRAME_SIZE;
                    currentMediaType.GetUINT64(ref frameSizeKey, out ulong frameSize);
                    width = (int)(frameSize >> 32);
                    height = (int)(frameSize & 0xFFFFFFFF);
                }
                finally
                {
                    Marshal.ReleaseComObject(currentMediaType);
                }

                if (width <= 0 || height <= 0) return frames;

                const int targetSize = ButtonImageRenderer.ButtonWidth; // 72 px

                // Obliczenie proporcji próbkowania w zależności od trybu dopasowania (Cover, Contain, Stretch)
                float baseScaleX, baseScaleY;
                string mode = (fitMode ?? "Cover").Trim();
                if (mode.Equals("Stretch", StringComparison.OrdinalIgnoreCase))
                {
                    baseScaleX = (float)width / targetSize;
                    baseScaleY = (float)height / targetSize;
                }
                else if (mode.Equals("Contain", StringComparison.OrdinalIgnoreCase))
                {
                    float fitScale = Math.Min((float)targetSize / width, (float)targetSize / height);
                    baseScaleX = 1.0f / fitScale;
                    baseScaleY = 1.0f / fitScale;
                }
                else // Cover (domyślnie)
                {
                    float fitScale = Math.Max((float)targetSize / width, (float)targetSize / height);
                    baseScaleX = 1.0f / fitScale;
                    baseScaleY = 1.0f / fitScale;
                }

                float zoomFactor = Math.Clamp(scale, 20, 400) / 100.0f;
                float finalScaleX = baseScaleX / zoomFactor;
                float finalScaleY = baseScaleY / zoomFactor;

                float cx = targetSize / 2.0f;
                float cy = targetSize / 2.0f;
                float halfSrcW = width / 2.0f;
                float halfSrcH = height / 2.0f;

                // Przesunięcie środka kadrowania w przestrzeni źródłowej wideo (-100% do +100%)
                // Pozwala na płynne przemieszczanie się aż do samych rogów i krawędzi wideo przy dowolnym zoomie (nawet 400%)
                float srcShiftX = (Math.Clamp(offsetX, -100, 100) / 100.0f) * halfSrcW;
                float srcShiftY = (Math.Clamp(offsetY, -100, 100) / 100.0f) * halfSrcH;
                float srcCenterX = halfSrcW + srcShiftX;
                float srcCenterY = halfSrcH - srcShiftY;

                byte[] destBytes = new byte[targetSize * targetSize * 4];
                using var frameBmp = new SKBitmap(targetSize, targetSize, SKColorType.Bgra8888, SKAlphaType.Premul);
                IntPtr pFramePixels = frameBmp.GetPixels();

                while (frames.Count < maxFrames)
                {
                    reader.ReadSample(videoStream, 0, out _, out uint flags, out _, out var sample);
                    if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0 || sample == null)
                        break;

                    sample.ConvertToContiguousBuffer(out var buffer);
                    try
                    {
                        buffer.Lock(out IntPtr pData, out _, out uint currentLength);
                        try
                        {
                            if (pData != IntPtr.Zero && currentLength >= (uint)(width * height * 3 / 2))
                            {
                                int allocatedHeight = (height + 15) & ~15;
                                int stride = (int)(currentLength / (allocatedHeight * 1.5));
                                if (stride < width) stride = width;
                                int uvOffset = stride * allocatedHeight;
                                int rawLength = (int)currentLength;

                                unsafe
                                {
                                    byte* pRaw = (byte*)pData;

                                    for (int y = 0; y < targetSize; y++)
                                    {
                                        float dy = (y + 0.5f) - cy;
                                        float srcYf = srcCenterY + dy * finalScaleY;
                                        int srcY = (int)srcYf;
                                        bool yInBounds = (srcY >= 0 && srcY < height);
                                        int uvY = srcY / 2;

                                        for (int x = 0; x < targetSize; x++)
                                        {
                                            float dx = (x + 0.5f) - cx;
                                            float srcXf = srcCenterX + dx * finalScaleX;
                                            int srcX = (int)srcXf;
                                            int destIdx = (y * targetSize + x) * 4;

                                            if (!yInBounds || srcX < 0 || srcX >= width)
                                            {
                                                destBytes[destIdx] = 0;
                                                destBytes[destIdx + 1] = 0;
                                                destBytes[destIdx + 2] = 0;
                                                destBytes[destIdx + 3] = 255;
                                                continue;
                                            }

                                            int uvX = (srcX / 2) * 2;
                                            int yIndex = srcY * stride + srcX;
                                            int uvIndex = uvOffset + uvY * stride + uvX;

                                            if (yIndex < 0 || yIndex >= rawLength || uvIndex < 0 || uvIndex + 1 >= rawLength)
                                            {
                                                destBytes[destIdx] = 0;
                                                destBytes[destIdx + 1] = 0;
                                                destBytes[destIdx + 2] = 0;
                                                destBytes[destIdx + 3] = 255;
                                                continue;
                                            }

                                            int yVal = pRaw[yIndex];
                                            int uVal = pRaw[uvIndex] - 128;
                                            int vVal = pRaw[uvIndex + 1] - 128;

                                            int r = Math.Clamp((int)(yVal + 1.402 * vVal), 0, 255);
                                            int g = Math.Clamp((int)(yVal - 0.344136 * uVal - 0.714136 * vVal), 0, 255);
                                            int b = Math.Clamp((int)(yVal + 1.772 * uVal), 0, 255);

                                            destBytes[destIdx] = (byte)b;
                                            destBytes[destIdx + 1] = (byte)g;
                                            destBytes[destIdx + 2] = (byte)r;
                                            destBytes[destIdx + 3] = 255;
                                        }
                                    }
                                }

                                Marshal.Copy(destBytes, 0, pFramePixels, destBytes.Length);
                                byte[] jpeg = ButtonImageRenderer.RenderBitmapJpeg(frameBmp);
                                frames.Add(jpeg);
                            }
                        }
                        finally
                        {
                            buffer.Unlock();
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(buffer);
                        Marshal.ReleaseComObject(sample);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }
        catch
        {
            // Graceful fallback on decode error
        }
        finally
        {
            MFShutdown();
        }

        return frames;
    }

    private static uint FindVideoStreamIndex(IMFSourceReader reader)
    {
        for (uint i = 0; i < 8; i++)
        {
            int hr = reader.GetNativeMediaType(i, 0, out var mt);
            if (hr == 0 && mt != null)
            {
                try
                {
                    mt.GetMajorType(out var maj);
                    if (maj == MFMediaType_Video)
                    {
                        return i;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(mt);
                }
            }
        }
        return 0;
    }
}

[ComImport]
[Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig]
    int GetStreamSelection([In] uint dwStreamIndex, [Out] out bool pfSelected);
    [PreserveSig]
    int SetStreamSelection([In] uint dwStreamIndex, [In] bool fSelected);
    [PreserveSig]
    int GetNativeMediaType([In] uint dwStreamIndex, [In] uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
    [PreserveSig]
    int GetCurrentMediaType([In] uint dwStreamIndex, out IMFMediaType ppMediaType);
    [PreserveSig]
    int SetCurrentMediaType([In] uint dwStreamIndex, [In] IntPtr pdwReserved, [In, MarshalAs(UnmanagedType.Interface)] IMFMediaType pMediaType);
    [PreserveSig]
    int SetStreamPosition(ref Guid guidTimeFormat, IntPtr pvarPosition);
    [PreserveSig]
    int ReadSample([In] uint dwStreamIndex, [In] uint dwControlFlags, out uint pdwActualStreamIndex, out uint pdwStreamFlags, out long pllTimestamp, out IMFSample ppSample);
    [PreserveSig]
    int Flush([In] uint dwStreamIndex);
    [PreserveSig]
    int GetServiceForStream([In] uint dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig]
    int GetPresentationAttribute([In] uint dwStreamIndex, ref Guid guidAttribute, IntPtr pAttribute);
}

[ComImport]
[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    void GetItem(ref Guid guidKey, IntPtr pValue);
    void GetItemType(ref Guid guidKey, out int pType);
    void CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
    void Compare(IntPtr pType, int ComparisonType, out bool pbResult);
    void GetUINT32(ref Guid guidKey, out uint punValue);
    void GetUINT64(ref Guid guidKey, out ulong punValue);
    void GetDouble(ref Guid guidKey, out double pfValue);
    void GetGUID(ref Guid guidKey, out Guid pguidValue);
    void GetStringLength(ref Guid guidKey, out uint pcchLength);
    void GetString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
    void GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
    void GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
    void GetBlob(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
    void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
    void GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void SetItem(ref Guid guidKey, IntPtr Value);
    void DeleteItem(ref Guid guidKey);
    void DeleteAllItems();
    void SetUINT32(ref Guid guidKey, uint unValue);
    void SetUINT64(ref Guid guidKey, ulong unValue);
    void SetDouble(ref Guid guidKey, double fValue);
    void SetGUID(ref Guid guidKey, ref Guid guidValue);
    void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
    void SetBlob(ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
    void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint pcItems);
    void GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
    void CopyAllItems(IntPtr pDest);

    void GetMajorType(out Guid pguidMajorType);
    void IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
    void IsEqual([MarshalAs(UnmanagedType.Interface)] IMFMediaType pIMediaType, out uint pdwFlags);
    void GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
    void FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
}

[ComImport]
[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    void GetItem(ref Guid guidKey, IntPtr pValue);
    void GetItemType(ref Guid guidKey, out int pType);
    void CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
    void Compare(IntPtr pType, int ComparisonType, out bool pbResult);
    void GetUINT32(ref Guid guidKey, out uint punValue);
    void GetUINT64(ref Guid guidKey, out ulong punValue);
    void GetDouble(ref Guid guidKey, out double pfValue);
    void GetGUID(ref Guid guidKey, out Guid pguidValue);
    void GetStringLength(ref Guid guidKey, out uint pcchLength);
    void GetString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
    void GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
    void GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
    void GetBlob(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
    void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
    void GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void SetItem(ref Guid guidKey, IntPtr Value);
    void DeleteItem(ref Guid guidKey);
    void DeleteAllItems();
    void SetUINT32(ref Guid guidKey, uint unValue);
    void SetUINT64(ref Guid guidKey, ulong unValue);
    void SetDouble(ref Guid guidKey, double fValue);
    void SetGUID(ref Guid guidKey, ref Guid guidValue);
    void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
    void SetBlob(ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
    void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint pcItems);
    void GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
    void CopyAllItems(IntPtr pDest);

    void GetSampleFlags(out uint pdwSampleFlags);
    void SetSampleFlags(uint dwSampleFlags);
    void GetSampleTime(out long phnsSampleTime);
    void SetSampleTime(long hnsSampleTime);
    void GetSampleDuration(out long phnsSampleDuration);
    void SetSampleDuration(long hnsSampleDuration);
    void GetBufferCount(out uint pdwBufferCount);
    void GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
    void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
    void AddBuffer(IMFMediaBuffer pBuffer);
    void RemoveBufferByIndex(uint dwIndex);
    void RemoveAllBuffers();
    void GetTotalLength(out uint pcbTotalLength);
    void CopyToBuffer(IMFMediaBuffer pBuffer);
}

[ComImport]
[Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    void Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
    void Unlock();
    void GetCurrentLength(out uint pcbCurrentLength);
    void SetCurrentLength(uint cbCurrentLength);
    void GetMaxLength(out uint pcbMaxLength);
}
