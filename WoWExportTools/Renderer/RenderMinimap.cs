﻿using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using WoWExportTools.Loaders;
using System.Drawing;
using System.Configuration;
using System.IO;
using CASCLib;
using static WoWExportTools.Structs;
using static WoWExportTools.Renderer.Structs;

namespace WoWExportTools.Renderer
{
    class RenderMinimap
    {
        public void Generate(MapTile mapTile, string outName, int bakeShaderProgram, bool loadModels = false)
        {
            var TileSize = 1600.0f / 3.0f; //533.333
            var ChunkSize = TileSize / 16.0f; //33.333
            var UnitSize = ChunkSize / 8.0f; //4.166666
            var MapMidPoint = 32.0f / ChunkSize;

            var bakeSize = 4096;
            var splitFiles = false;

            ConfigurationManager.RefreshSection("appSettings");
            switch (ConfigurationManager.AppSettings["bakeQuality"])
            {
                case "minimap": bakeSize = 256; break;
                case "low": bakeSize = 4096; break;
                case "medium": bakeSize = 8192; break;
                case "high":
                    bakeSize = 1024;
                    splitFiles = true;
                    break;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outName));

            Terrain terrain = ADTLoader.LoadADT(mapTile, bakeShaderProgram, loadModels);
            GL.ClearColor(Color.Black);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(bakeShaderProgram);

            // Look up uniforms beforehand instead of during drawing
            var firstPos = terrain.startPos.Position;
            var projectionMatrixLocation = GL.GetUniformLocation(bakeShaderProgram, "projection_matrix");
            var modelviewMatrixLocation = GL.GetUniformLocation(bakeShaderProgram, "modelview_matrix");
            var firstPosLocation = GL.GetUniformLocation(bakeShaderProgram, "firstPos");
            var doodadOffsLocation = GL.GetUniformLocation(bakeShaderProgram, "doodadOffs");
            var heightScaleLoc = GL.GetUniformLocation(bakeShaderProgram, "pc_heightScale");
            var heightOffsetLoc = GL.GetUniformLocation(bakeShaderProgram, "pc_heightOffset");

            var layerLocs = new int[4];
            var scaleLocs = new int[4];
            var heightLocs = new int[4];
            var blendLocs = new int[4];

            for (var i = 0; i < 4; i++)
            {
                layerLocs[i] = GL.GetUniformLocation(bakeShaderProgram, "pt_layer" + i);
                scaleLocs[i] = GL.GetUniformLocation(bakeShaderProgram, "layer" + i + "scale");
                heightLocs[i] = GL.GetUniformLocation(bakeShaderProgram, "pt_height" + i);

                // There are only 3 blend samplers
                if (i > 0)
                {
                    blendLocs[i] = GL.GetUniformLocation(bakeShaderProgram, "pt_blend" + i);
                }
            }

            if (splitFiles)
            {
                GL.BindVertexArray(terrain.vao);

                for (var i = 0; i < terrain.renderBatches.Length; i++)
                {
                    var x = i / 16;
                    var y = i % 16;

                    var frameBuffer = GL.GenFramebuffer();
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);

                    var bakedTexture = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, bakedTexture);

                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, bakeSize, bakeSize, 0, PixelFormat.Rgb, PixelType.UnsignedByte, new IntPtr(0));
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                    GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, bakedTexture, 0);

                    GL.DrawBuffers(1, new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0 });

                    if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                    {
                        var fberror = GL.GetError().ToString();
                        Logger.WriteLine("Frame buffer initialization error: " + fberror);
                    }

                    var projectionMatrix = Matrix4.CreateOrthographic(ChunkSize, ChunkSize, -1500f, 1500f);
                    GL.UniformMatrix4(projectionMatrixLocation, false, ref projectionMatrix);

                    var eye = new Vector3(-ChunkSize / 2, (-ChunkSize / 2), 400f);
                    var target = new Vector3(-ChunkSize / 2, (-ChunkSize / 2), 399.9999f);

                    var modelViewMatrix = Matrix4.LookAt(eye, target, new Vector3(0f, 1f, 0f));
                    GL.UniformMatrix4(modelviewMatrixLocation, false, ref modelViewMatrix);

                    var chunkPos = firstPos;
                    chunkPos.X -= ChunkSize * x;
                    chunkPos.Y -= ChunkSize * y;

                    GL.Uniform3(firstPosLocation, ref chunkPos);
                    GL.Viewport(0,0, bakeSize, bakeSize);
                    GL.Uniform4(heightScaleLoc, terrain.renderBatches[i].heightScales);
                    GL.Uniform4(heightOffsetLoc, terrain.renderBatches[i].heightOffsets);

                    for (var j = 0; j < terrain.renderBatches[i].materialID.Length; j++)
                    {
                        GL.Uniform1(layerLocs[j], j);
                        GL.Uniform1(scaleLocs[j], terrain.renderBatches[i].scales[j]);

                        GL.ActiveTexture(TextureUnit.Texture0 + j);
                        GL.BindTexture(TextureTarget.Texture2D, (int)terrain.renderBatches[i].materialID[j]);
                    }

                    for (var j = 1; j < terrain.renderBatches[i].alphaMaterialID.Length; j++)
                    {
                        GL.Uniform1(blendLocs[j], 3 + j);

                        GL.ActiveTexture(TextureUnit.Texture3 + j);
                        GL.BindTexture(TextureTarget.Texture2D, terrain.renderBatches[i].alphaMaterialID[j]);
                    }

                    for (var j = 0; j < terrain.renderBatches[i].heightMaterialIDs.Length; j++)
                    {
                        GL.Uniform1(heightLocs[j], 7 + j);

                        GL.ActiveTexture(TextureUnit.Texture7 + j);
                        GL.BindTexture(TextureTarget.Texture2D, terrain.renderBatches[i].heightMaterialIDs[j]);
                    }

                    GL.DrawElements(PrimitiveType.Triangles, (int)terrain.renderBatches[i].numFaces, DrawElementsType.UnsignedInt, (int)terrain.renderBatches[i].firstFace * 4);

                    for (var j = 0; j < 11; j++)
                    {
                        GL.ActiveTexture(TextureUnit.Texture0 + j);
                        GL.BindTexture(TextureTarget.Texture2D, 0);
                    }

                    var error = GL.GetError().ToString();
                    if (error != "NoError")
                        Logger.WriteLine("Drawing error: " + error);

                    var bmp = new Bitmap(bakeSize, bakeSize, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    GL.ReadPixels(0, 0, bakeSize, bakeSize, PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);
                    bmp.UnlockBits(data);

                    bmp.RotateFlip(RotateFlipType.Rotate270FlipX);
                    bmp.Save(outName.Replace(".png", "_" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);

                    bmp.Dispose();

                    GL.DeleteTexture(bakedTexture);
                    GL.DeleteFramebuffer(frameBuffer);
                }
                
                GL.UseProgram(0);
            }
            else
            {
                var frameBuffer = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);

                var bakedTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, bakedTexture);

                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, bakeSize, bakeSize, 0, PixelFormat.Rgb, PixelType.UnsignedByte, new IntPtr(0));
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, bakedTexture, 0);

                GL.DrawBuffers(1, new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0 });

                if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                {
                    var fberror = GL.GetError().ToString();
                    Logger.WriteLine("Frame buffer initialization error: " + fberror);
                }

                var projectionMatrix = Matrix4.CreateOrthographic(TileSize, TileSize, -1500f, 1500f);
                GL.UniformMatrix4(projectionMatrixLocation, false, ref projectionMatrix);

                var eye = new Vector3(-TileSize / 2, -TileSize / 2, 400f);
                var target = new Vector3(-TileSize / 2, -TileSize / 2, 399.9999f);
                var modelViewMatrix = Matrix4.LookAt(eye, target, new Vector3(0f, 1f, 0f));
                GL.UniformMatrix4(modelviewMatrixLocation, false, ref modelViewMatrix);

                GL.Uniform3(firstPosLocation, ref firstPos);

                GL.Viewport(0, 0, bakeSize, bakeSize);

                GL.BindVertexArray(terrain.vao);

                for (var i = 0; i < terrain.renderBatches.Length; i++)
                {
                    GL.Uniform4(heightScaleLoc, terrain.renderBatches[i].heightScales);
                    GL.Uniform4(heightOffsetLoc, terrain.renderBatches[i].heightOffsets);

                    for (var j = 0; j < terrain.renderBatches[i].materialID.Length; j++)
                    {
                        GL.Uniform1(layerLocs[j], j);
                        GL.Uniform1(scaleLocs[j], terrain.renderBatches[i].scales[j]);

                        GL.ActiveTexture(TextureUnit.Texture0 + j);
                        GL.BindTexture(TextureTarget.Texture2D, (int)terrain.renderBatches[i].materialID[j]);
                    }

                    for (var j = 1; j < terrain.renderBatches[i].alphaMaterialID.Length; j++)
                    {
                        GL.Uniform1(blendLocs[j], 3 + j);

                        GL.ActiveTexture(TextureUnit.Texture3 + j);
                        GL.BindTexture(TextureTarget.Texture2D, terrain.renderBatches[i].alphaMaterialID[j]);
                    }

                    for (var j = 0; j < terrain.renderBatches[i].heightMaterialIDs.Length; j++)
                    {
                        GL.Uniform1(heightLocs[j], 7 + j);

                        GL.ActiveTexture(TextureUnit.Texture7 + j);
                        GL.BindTexture(TextureTarget.Texture2D, terrain.renderBatches[i].heightMaterialIDs[j]);
                    }

                    GL.DrawElements(PrimitiveType.Triangles, (int)terrain.renderBatches[i].numFaces, DrawElementsType.UnsignedInt, (int)terrain.renderBatches[i].firstFace * 4);

                    for (var j = 0; j < 11; j++)
                    {
                        GL.ActiveTexture(TextureUnit.Texture0 + j);
                        GL.BindTexture(TextureTarget.Texture2D, 0);
                    }
                }

                var error = GL.GetError().ToString();
                if (error != "NoError")
                    Logger.WriteLine("Drawing error: " + error);

                try
                {
                    using (var bmp = new Bitmap(bakeSize, bakeSize))
                    {
                        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        GL.ReadPixels(0, 0, bakeSize, bakeSize, PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);
                        bmp.UnlockBits(data);

                        bmp.RotateFlip(RotateFlipType.Rotate270FlipX);
                        bmp.Save(outName, System.Drawing.Imaging.ImageFormat.Png);

                        bmp.Dispose();
                    }
                }
                catch (Exception e)
                {
                    Logger.WriteLine("An error occured while baking minimap image " + Path.GetFileNameWithoutExtension(outName) + ": " + e.StackTrace);
                }
                finally
                {
                    GL.DeleteTexture(bakedTexture);
                    GL.DeleteFramebuffer(frameBuffer);
                    GL.UseProgram(0);
                }
            }

            // Clean-up.
            foreach (var batch in terrain.renderBatches)
                GL.DeleteTextures(batch.alphaMaterialID.Length, batch.alphaMaterialID);

            GC.Collect();
        }
    }
}
