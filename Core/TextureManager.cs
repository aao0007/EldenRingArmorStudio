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

            using var stream = new MemoryStream(textureData);
            using IImage image = Pfimage.FromStream(stream);

            // Pfim añade padding al final de cada fila para alinear a 4 bytes.
            // Si stride != width*bpp, GL interpreta el relleno como píxeles
            // y la textura aparece con manchas/franjas de colores incorrectos.
            int bpp = image.Format == ImageFormat.Rgb24 ? 3 : 4;
            int cleanStride = image.Width * bpp;

            byte[] pixels;
            if (image.Stride != cleanStride)
            {
                pixels = new byte[cleanStride * image.Height];
                for (int row = 0; row < image.Height; row++)
                    System.Buffer.BlockCopy(
                        image.Data, row * image.Stride,
                        pixels, row * cleanStride,
                        cleanStride);
            }
            else
            {
                pixels = image.Data;
            }

            PixelInternalFormat internalFormat = image.Format == ImageFormat.Rgb24
                ? PixelInternalFormat.Rgb8 : PixelInternalFormat.Rgba8;
            PixelFormat format = image.Format == ImageFormat.Rgb24
                ? PixelFormat.Bgr : PixelFormat.Bgra;

            int textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Sin alineación extra — ya quitamos el padding manualmente
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                image.Width, image.Height, 0,
                format, PixelType.UnsignedByte, pixels);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4); // restaurar

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Filtrado anisotrópico — elimina pixelado en superficies en ángulo
            GL.GetFloat((GetPName)0x84FF, out float maxAniso);
            if (maxAniso > 0f)
                GL.TexParameter(TextureTarget.Texture2D,
                    (TextureParameterName)0x84FE, maxAniso);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            return textureId;
        }
    }
}