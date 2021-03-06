﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static partial class ADT {

    private static long MCNKchnkPos;
    private static int MCNKsize;

    private static void ReadMAMP (Stream ADTtexstream)
    {
        string MAMP = ReadFourCC(ADTtexstream);
        int MAMPsize = ReadLong(ADTtexstream);
        if (MAMP != "MAMP")
            Debug.Log("Wrong Data Type : " + MAMP);
        int texture_size = ReadLong(ADTtexstream); // either defined here or in MHDR.mamp_value.
    }

    private static void ReadMTEX (Stream ADTtexstream)
    {
        if (ADTtexstream.Length == ADTtexstream.Position)
            return;

        string MTEX = ReadFourCC(ADTtexstream);
        int MTEXsize = ReadLong(ADTtexstream);
        if (MTEX != "MTEX")
            Debug.Log("Wrong Data Type : " + MTEX);

        // texture path strings, separated by 0
        string texturePath = "";
        int numberOfTextures = 0;
        for (int a = 0; a < MTEXsize; a++)
	    {
            int b = ADTtexstream.ReadByte();
            if (b != 0)
            {
                var stringSymbol = System.Convert.ToChar(b);
                texturePath = texturePath + stringSymbol;
            }
            else if (b == 0)
            {
                blockData.terrainTexturePaths.Add(texturePath);
                string extractedPath = Casc.GetFile(texturePath);
                Stream stream = File.Open(extractedPath, FileMode.Open);
                //Debug.Log(extractedPath);
                byte[] data = BLP.GetUncompressed(stream, true);
                BLPinfo info = BLP.Info();
                
                Texture2Ddata texture2Ddata = new Texture2Ddata();
                texture2Ddata.hasMipmaps = info.hasMipmaps;
                texture2Ddata.width = info.width;
                texture2Ddata.height = info.height;
                if (info.width != info.height) // Unity doesn't support nonsquare mipmaps // sigh
                    texture2Ddata.hasMipmaps = false;
                texture2Ddata.textureFormat = info.textureFormat;
                texture2Ddata.TextureData = data;
                blockData.terrainTextures.Add(texturePath, texture2Ddata);
                texturePath = null;
                numberOfTextures++;
                stream.Close();
                stream=null;
            }
        }
    }

    private static void ReadMCNKtex (Stream ADTtexstream, string mapname)
    {
        if (ADTtexstream.Length == ADTtexstream.Position)
            return;

        for (int i = 0; i < 256; i++)
        {
            string MCNK = ReadFourCC(ADTtexstream);
            MCNKsize = ReadLong(ADTtexstream);
            if (MCNK != "MCNK")
            {
                Debug.Log("Wrong Data Type : " + MCNK);
            }
            MCNKchnkPos = ADTtexstream.Position;

            // Subchunks
            ReadMCLY(ADTtexstream, i); // texture layers
            ReadMCSH(ADTtexstream, i); // static shadow maps
            ReadMCAL(ADTtexstream, mapname, i); // Alpha maps

            ADTtexstream.Seek(MCNKchnkPos + MCNKsize, SeekOrigin.Begin);
        }
    }

    /////////////////////////////
    ///// MCNKtex Subchunks /////
    /////////////////////////////

    private static void ReadMCLY(Stream ADTtexstream, int chunk)
    {
        /*
        *  Texture layer definitions for this map chunk. 16 bytes per layer, up to 4 layers (thus, layer count = size / 16).
        *  Every texture layer other than the first will have an alpha map to specify blending amounts. The first layer is rendered with full opacity. To know which alphamap is used, there is an offset into the MCAL chunk. That one is relative to MCAL.
        *  You can animate these by setting the flags. Only simple linear animations are possible. You can specify the direction in 45° steps and the speed.
        *  The textureId is just the array index of the filename array in the MTEX chunk.
        *  For getting the right feeling when walking, you should set the effectId which links to GroundEffectTextureRec::m_ID. It defines the little detail doodads as well as the footstep sounds and if footprints are visible. You can set the id to -1 (int16!) to have no detail doodads and footsteps at all. Also, you need to define the currently on-top layer in the MCNK structure for the correct detail doodads to show up!
        *  Introduced in Wrath of the Lich King, terrain can now reflect a skybox. This is used for icecubes made out of ADTs to reflect something. You need to have the MTXF chunk in, if you want that. Look at an skybox Blizzard made to see how you should do it.
        */
        string MCLY = ReadFourCC(ADTtexstream);
        int MCLYsize = ReadLong(ADTtexstream);
        if (MCLY != "MCLY")
            Debug.Log("Wrong Data Type : " + MCLY);
        if (MCLYsize == 0)
        {
            return;
        }

        long MCLYStartPosition = ADTtexstream.Position;
        int numberOfLayers = MCLYsize / 16;
        blockData.ChunksData[chunk].NumberOfTextureLayers = numberOfLayers;

        for (int l = 0; l < numberOfLayers; l++)
        {
            blockData.ChunksData[chunk].textureIds[l] = ReadLong(ADTtexstream); // texture ID
            // <flags>
            byte[] arrayOfBytes = new byte[4];
            ADTtexstream.Read(arrayOfBytes, 0, 4);
            BitArray flags = new BitArray(arrayOfBytes);
            int animation_rotation = (flags[0] ? 1 : 0) + (flags[1] ? 1 : 0) + (flags[2] ? 1 : 0); // each tick is 45°
            int animation_speed = (flags[3] ? 1 : 0) + (flags[4] ? 1 : 0) + (flags[5] ? 1 : 0); // 0 to 3
            bool animation_enabled = flags[6];
            bool overbright = flags[7];                // This will make the texture way brighter. Used for lava to make it "glow".
            bool use_alpha_map = flags[8];             // set for every layer after the first
            blockData.ChunksData[chunk].alpha_map_compressed[l] = flags[9];      // see MCAL chunk description - MCLY_AlphaType_Flag
            bool use_cube_map_reflection = flags[10];   // This makes the layer behave like its a reflection of the skybox. See below
            bool unknown_0x800 = flags[11];             // WoD?+ if either of 0x800 or 0x1000 is set, texture effects' texture_scale is applied
            bool unknown_0x1000 = flags[12];            // WoD?+ see 0x800
            // flags 13-32 unused
            // </flags>
            int layerOffset = ReadLong(ADTtexstream);
            blockData.ChunksData[chunk].LayerOffsetsInMCAL[l] = layerOffset;
            int effectId = ReadLong(ADTtexstream); //foreign_keyⁱ <uint32_t, &GroundEffectTextureRec::m_ID>; // 0xFFFFFFFF for none, in alpha: uint16_t + padding
        }
        //Debug.Log(MCLYStartPosition + " " + ADTtexstream.Position + " " + MCLYsize);
    }

    private static void ReadMCSH(Stream ADTtexstream, int chunk) //512 bytes
    {
        if (has_mcsh[chunk] != true)
            return;

        string MCSH = ReadFourCC(ADTtexstream);
        int MCSHsize = ReadLong(ADTtexstream);
        if (MCSH != "MCSH")
            Debug.Log("Wrong Data Type : " + MCSH);

        /* skip
        // The shadows are stored per bit, not byte as 0 or 1 (off or on) so we have 8 bytes (which equates to 64 values) X 64 bytes (64 values in this case) which ends up as a square 64x64 shadowmap with either white or black.
        // Note that the shadow values come LSB first.
        // 8bytes(64values) x 64 = 512 bytes
        bool[,] shadowMap = new bool[64, 64];
        for (int r = 0; r<64; r++)
        {
            byte[] ByteRow = new byte[8];
            ADTtexstream.Read(ByteRow, 0, 8);
            BitArray bitRow = new BitArray(ByteRow);

            for (int c = 63; c >= 0; c--) // LSB
            {
                shadowMap[r, c] = bitRow[c]; // need to test if it's [r,c] or [c,r] 
            }
        }
        */
        //Debug("Got shadow..");
        ADTtexstream.Seek(MCSHsize, SeekOrigin.Current);
    }

    private static void ReadMCAL(Stream ADTtexstream, string mapname, int chunk)
    {
        if (ADTtexstream.Length <= ADTtexstream.Position)
            return;
        string chunky = ReadFourCC(ADTtexstream);
        if (chunky != "MCAL") // should replace with PeekFourCC to simplify
        {
            //Debug.Log(chunky);
            return;
        }
        ADTtexstream.Seek(-4, SeekOrigin.Current);

        string MCAL = ReadFourCC(ADTtexstream);
        int MCALsize = ReadLong(ADTtexstream);
        if (MCAL != "MCAL")
            Debug.Log("Wrong Data Type : " + MCAL);

        long McalStartPosition = ADTtexstream.Position;
        int numberofLayers = blockData.ChunksData[chunk].NumberOfTextureLayers;
        if (numberofLayers > 1)
        {
            blockData.ChunksData[chunk].alphaLayers = new List<byte[]>();
            for (int l = 1; l < numberofLayers; l++) {

                //ADTtexstream.Seek(blockData.ChunksData[chunk].LayerOffsetsInMCAL[l] + McalStartPosition, SeekOrigin.Begin);

                if (WDT.Flags[mapname].adt_has_height_texturing == true)
                {
                    if (blockData.ChunksData[chunk].alpha_map_compressed[l] == false)
                    {
                        blockData.ChunksData[chunk].alphaLayers.Add(AlphaMap_UncompressedFullRes(ADTtexstream));
                    }
                    else if (blockData.ChunksData[chunk].alpha_map_compressed[l] == true)
                    {
                        blockData.ChunksData[chunk].alphaLayers.Add(AlphaMap_Compressed(ADTtexstream));
                    }
                }
                else if (WDT.Flags[mapname].adt_has_height_texturing == false)
                {
                    if (WDT.Flags[mapname].adt_has_big_alpha == false)
                    {
                        if (blockData.ChunksData[chunk].alpha_map_compressed[l] == false)
                        {
                            blockData.ChunksData[chunk].alphaLayers.Add(AlphaMap_UncompressedHalfRes(ADTtexstream));
                        }
                        else if (blockData.ChunksData[chunk].alpha_map_compressed[l] == true)
                        {
                            blockData.ChunksData[chunk].alphaLayers.Add(AlphaMap_Compressed(ADTtexstream));
                        }
                    }
                    else if (WDT.Flags[mapname].adt_has_big_alpha == true)
                    {
                        if (blockData.ChunksData[chunk].alpha_map_compressed[l] == false)
                        {
                            blockData.ChunksData[chunk].alphaLayers.Add(AlphaMap_UncompressedFullRes(ADTtexstream));
                        }
                        else if (blockData.ChunksData[chunk].alpha_map_compressed[l] == true)
                        {
                            blockData.ChunksData[chunk].alphaLayers.Add(AlphaMap_Compressed(ADTtexstream));
                        }
                    }
                }
            }
        }
    }

    private static byte[] AlphaMap_UncompressedFullRes(Stream ADTtexstream) // uncompressed 4096 bytes
    {
        byte[] textureArray = new byte[4096];
        ADTtexstream.Read(textureArray, 0, 4096);
        return textureArray;
    }

    private static byte[] AlphaMap_Compressed(Stream ADTtexstream) // compressed
    {
        byte[] textureArray = new byte[4096];
        int alphaOffset = 0;
        while (alphaOffset < 4096)
        {
            //read a byte//
            byte onebyte = (byte)ADTtexstream.ReadByte();
            //translate byte to 8 bits//
            byte[] bytearr = new byte[1];
            bytearr[0] = onebyte;
            BitArray bitarr = new BitArray(bytearr);
            //is first bit true?//
            bool fc = bitarr.Get(7); //true=fill, false=copy
            //next 7 bits determine how many times we fill/copy, max 127
            int[] array  = new int[1];
            bool[] bitArray = new bool[7];
            for (int i = 0; i < 7; i++) {
                bitArray[i] = bitarr.Get(6 - i);
            }
            int times = BoolArrayToInt(bitArray);
            if (times == 0)
            {
                alphaOffset = 4096;
                break;
            }
            if (times != 0)
            {
                //fill mode// 
                if (fc == true) // repeat the byte following the one we just read *count* number of times into the alpha map
                {
                    byte secondbytefill = (byte)ADTtexstream.ReadByte();
                    for (int j = 0; j < times; j++){
                        textureArray[alphaOffset] = secondbytefill;
                        alphaOffset++;
                    }
                }
                //copy mode//
                if (fc == false)  //  read *count* number of following bytes into the alpha map
                {
                    for (int jc = 0; jc < times; jc++) {
                        byte secondbytecopy = (byte)ADTtexstream.ReadByte();
                        textureArray[alphaOffset] = secondbytecopy;
                        alphaOffset++;
                    }
                }
            }
        }
        alphaOffset = 0;
        return textureArray;
    }

    private static byte[] AlphaMap_UncompressedHalfRes(Stream ADTtexstream)
    {
        int currentArrayPos = 0;
        byte[] textureArray = new byte[4096];
        for (int ux = 0 ; ux < 2048; ux++){
            byte onebyte = (byte)ADTtexstream.ReadByte();
            byte nibble1 = (byte)(onebyte & 0x0F);
            byte nibble2 = (byte)((onebyte & 0xF0) >> 4);
            int first = NormalizeHalfResAlphaPixel(nibble2);
            int second = NormalizeHalfResAlphaPixel(nibble1);
            textureArray[ux + currentArrayPos + 0] = (byte)first;
            textureArray[ux + currentArrayPos + 1] = (byte)second;
            currentArrayPos = currentArrayPos + 1;
        }
        currentArrayPos = 0;
        return textureArray;
    }
}
