using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using Pfim;

namespace EldenRingArmorStudio.Core
{
    public static class TextureManager
    {
        public static int LoadDdsTextureFromBytes(byte[] textureData)
        {
            if (textureData == null || textureData.Length == 0) return -1;

            using (var stream = new MemoryStream(textureData))
            using (IImage image = Pfim.Pfimage.FromStream(stream))
            {
                int textureId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, textureId);

                PixelInternalFormat internalFormat;
                PixelFormat format;

                // Pfim descomprime automáticamente DXT a Rgba32 o Rgb24
                if (image.Format == Pfim.ImageFormat.Rgba32)
                {
                    internalFormat = PixelInternalFormat.Rgba;
                    format = PixelFormat.Bgra;
                }
                else
                {
                    internalFormat = PixelInternalFormat.Rgb;
                    format = PixelFormat.Bgr;
                }

                // OpenTK 4 requiere el uso de punteros (unsafe/IntPtr) para pasar los datos de la imagen
                unsafe
                {
                    fixed (byte* ptr = image.Data)
                    {
                        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                            image.Width, image.Height, 0, format, PixelType.UnsignedByte, (IntPtr)ptr);
                    }
                }

                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                return textureId;
            }
        }
    }
}