using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using Pfim;

namespace EldenRingArmorStudio.Core
{
    public static class TextureManager
    {
        public static int LoadDdsTexture(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"No se encontró la textura DDS: {filePath}");

            using (IImage image = Pfim.Pfim.FromStream(File.OpenRead(filePath)))
            {
                int textureId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, textureId);

                PixelInternalFormat internalFormat;
                PixelFormat pixelFormat;
                PixelType pixelType = PixelType.UnsignedByte;

                // Mapear el formato DDS decodificado por Pfim al formato nativo de OpenGL
                switch (image.Format)
                {
                    case Pfim.ImageFormat.Rgb24:
                        internalFormat = PixelInternalFormat.Rgb8;
                        pixelFormat = PixelFormat.Bgr;
                        break;
                    case Pfim.ImageFormat.Rgba32:
                        internalFormat = PixelInternalFormat.Rgba8;
                        pixelFormat = PixelFormat.Bgra;
                        break;
                    default:
                        // Si es un formato comprimido (BC1/BC3/DXT), Pfim expone los datos directamente
                        internalFormat = GetCompressedFormat(image.Format);
                        UploadCompressedTexture(image, internalFormat);
                        SetTextureParameters();
                        return textureId;
                }

                // Subida para texturas no comprimidas
                GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, image.Width, image.Height,
                    0, pixelFormat, pixelType, image.Data);

                SetTextureParameters();
                return textureId;
            }
        }

        private static void UploadCompressedTexture(IImage image, PixelInternalFormat format)
        {
            // OpenGL maneja la carga de texturas con compresión de bloques nativa (DXT/BC)
            GL.CompressedTexImage2D(TextureTarget.Texture2D, 0, format,
                image.Width, image.Height, 0, image.DataLen, image.Data);
        }

        private static PixelInternalFormat GetCompressedFormat(Pfim.ImageFormat format)
        {
            return format switch
            {
                Pfim.ImageFormat.Dxt1 => PixelInternalFormat.CompressedRgbaS3tcDxt1Ext,
                Pfim.ImageFormat.Dxt3 => PixelInternalFormat.CompressedRgbaS3tcDxt3Ext,
                Pfim.ImageFormat.Dxt5 => PixelInternalFormat.CompressedRgbaS3tcDxt5Ext,
                _ => throw new NotSupportedException($"Formato DDS de Elden Ring no soportado en este cargador: {format}")
            };
        }

        private static void SetTextureParameters()
        {
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }
    }
}