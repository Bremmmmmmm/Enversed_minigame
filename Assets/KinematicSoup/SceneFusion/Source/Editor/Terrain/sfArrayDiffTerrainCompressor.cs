//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using KS.Compression;

//namespace KS.SceneFusion2.Unity.Editor
//{
//    class sfArrayDiffTerrainCompressor : sfITerrainCompressor
//    {
//        // Mantissa: 4 bytes
//        // Exponent: 2 bytes
//        // Sign: 1 byte
//        private const int FloatDiffSize = 7;
//        // Mantissa: 1 sign bit and other 24 bits
//        // Exponent: 1 sign bit and other 9 bits
//        // Sign: 1 bit
//        private const int FloatDiffBitNumToEncode = 36;

//        private Object2dArrayEncoder.EncodeParams m_heightmapEncodeParams = new Object2dArrayEncoder.EncodeParams(
//            null,//new byte[REGION_RESOLUTION * REGION_RESOLUTION * sizeof(float)],
//            0,//REGION_RESOLUTION,
//            0,//REGION_RESOLUTION,
//            sizeof(float),
//            FloatDiffSize,
//            FloatDiffBitNumToEncode,
//            DiffFunctions.DiffSingleFloatComponent,
//            EncodeFunctions.EncodeSingleFloatComponents);
//        private Object2dArrayDecoder.DecodeParams m_heightmapDecodeParams = new Object2dArrayDecoder.DecodeParams(
//            null,
//            0,//REGION_RESOLUTION,
//            0,//REGION_RESOLUTION,
//            sizeof(float),
//            FloatDiffSize,
//            FloatDiffBitNumToEncode,
//            ReverseDiffFunctions.ReverseDiffSingleFloatComponents,
//            DecodeFunctions.DecodeSingleFloatComponents);

//        private Object2dArrayEncoder m_encoder = new Object2dArrayEncoder();
//        private Object2dArrayDecoder m_decoder = new Object2dArrayDecoder();

//        /// <summary>Encodes heightmap data./summary>
//        /// <param name="heightmapData"></param>
//        /// <returns>Encoded data</returns>
//        public byte[] EncodeHeightmap(float[,] heightmapData)
//        {
//            bool resize = false;
//            if (m_heightmapEncodeParams.ColumnCount != heightmapData.GetLength(0))
//            {
//                m_heightmapEncodeParams.ColumnCount = heightmapData.GetLength(0);
//                resize = true;
//            }
//            if (m_heightmapEncodeParams.RowCount != heightmapData.GetLength(1))
//            {
//                m_heightmapEncodeParams.RowCount = heightmapData.GetLength(1);
//                resize = true;
//            }
//            if (resize)
//            {
//                m_heightmapEncodeParams.Source = 
//                    new byte[heightmapData.GetLength(0) * heightmapData.GetLength(1) * sizeof(float)];
//            }
//            Buffer.BlockCopy(heightmapData, 0, m_heightmapEncodeParams.Source, 0, m_heightmapEncodeParams.Source.Length);
//            return m_encoder.Encode(m_heightmapEncodeParams);
//        }

//        /// <summary>Decodes heightmap data.</summary>
//        /// <param name="data">Data to decode.</param>
//        /// <param name="width">Width of the region the data is for.</param>
//        /// <param name="height">Height of the region the data is for.</param>
//        /// <returns>Decoded heightmap data</returns>
//        public float[,] DecodeHeightmap(byte[] data, int width, int height)
//        {
//            m_heightmapDecodeParams.Encoded = data;
//            m_heightmapDecodeParams.ColumnCount = height;
//            m_heightmapDecodeParams.RowCount = width;
//            byte[] decodedData = m_decoder.Decode(m_heightmapDecodeParams);
//            float[,] heightmapData = new float[height, width];
//            Buffer.BlockCopy(decodedData, 0, heightmapData, 0, decodedData.Length);
//            return heightmapData;
//        }

//        /// <summary>Encodes alphamap data.</summary>
//        /// <param name="alphamapData"></param>
//        /// <returns>Encoded data</returns>
//        public byte[] EncodeAlphamap(float[,,] alphamapData)
//        {
//            byte[] data = new byte[alphamapData.GetLength(0) * alphamapData.GetLength(1) * alphamapData.GetLength(2) * sizeof(float)];
//            Buffer.BlockCopy(alphamapData, 0, data, 0, data.Length);
//            return data;
//        }

//        /// <summary>Decodes alphamap data.</summary>
//        /// <param name="data">Data to decode.</param>
//        /// <param name="width">Width of the region the data is for.</param>
//        /// <param name="height">Height of the region the data is for.</param>
//        /// <param name="numLayers">Number of alphamap layers.</param>
//        /// <returns>Decoded alphamap data</returns>
//        public float[,,] DecodeAlphamap(byte[] data, int width, int height, int numLayers)
//        {
//            int encodedLayers = data.Length / (4 * height * width);
//            float[,,] alphamapData = new float[height, width, encodedLayers];
//            Buffer.BlockCopy(data, 0, alphamapData, 0, data.Length);
//            // Sometimes the number of layers in the encoded data is different than the current number of layers if new
//            // layers were added since the data was encoded. The new layers are always on the end, so we copy the data
//            // into a corrected data array that has the new layers with their values set to zero.
//            if (encodedLayers != numLayers)
//            {
//                float[,,] correctedData = new float[height, width, numLayers];
//                for (int x = 0; x < width; x++)
//                {
//                    for (int y = 0; y < height; y++)
//                    {
//                        for (int i = 0; i < encodedLayers; i++)
//                        {
//                            correctedData[y, x, i] = alphamapData[y, x, i];
//                        }
//                    }
//                }
//                return correctedData;
//            }
//            return alphamapData;
//        }

//        /// <summary>Encodes detail layer data.</summary>
//        /// <param name="detailLayerData"></param>
//        /// <returns>Encoded data</returns>
//        public byte[] EncodeDetailLayer(int[,] detailLayerData)
//        {
//            byte[] data = new byte[detailLayerData.GetLength(0) * detailLayerData.GetLength(1) * sizeof(int)];
//            Buffer.BlockCopy(detailLayerData, 0, data, 0, data.Length);
//            return data;
//        }

//        /// <summary>Decodes detail layer data.</summary>
//        /// <param name="data">Data to decode.</param>
//        /// <param name="width">Width of the region the data is for.</param>
//        /// <param name="height">Height of the region the data is for.</param>
//        /// <returns>Decoded detail layer data</returns>
//        public int[,] DecodeDetailLayer(byte[] data, int width, int height)
//        {
//            int[,] detailLayerData = new int[height, width];
//            Buffer.BlockCopy(data, 0, detailLayerData, 0, data.Length);
//            return detailLayerData;
//        }
//    }
//}
