﻿using System;
using System.IO;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pqc.Crypto.SphincsPlus;
using Org.BouncyCastle.Security;

namespace Org.BouncyCastle.Crypto.Signers
{
    public sealed class SlhDsaSigner
        : ISigner
    {
        private readonly Buffer m_buffer = new Buffer();
        private readonly bool m_deterministic;

        private SlhDsaPrivateKeyParameters m_privateKey;
        private SlhDsaPublicKeyParameters m_publicKey;
        private SecureRandom m_random;
        private SphincsPlusEngine m_engine;

        public SlhDsaSigner()
            : this(deterministic: false)
        {
        }

        public SlhDsaSigner(bool deterministic)
        {
            m_deterministic = deterministic;
        }

        public string AlgorithmName => "SLH-DSA";

        public void Init(bool forSigning, ICipherParameters parameters)
        {
            byte[] providedContext = null;
            if (parameters is ParametersWithContext withContext)
            {
                if (withContext.ContextLength > 255)
                    throw new ArgumentOutOfRangeException("context too long", nameof(parameters));

                providedContext = withContext.GetContext();
                parameters = withContext.Parameters;
            }

            if (forSigning)
            {
                SecureRandom providedRandom = null;
                if (parameters is ParametersWithRandom withRandom)
                {
                    providedRandom = withRandom.Random;
                    parameters = withRandom.Parameters;
                }

                m_privateKey = (SlhDsaPrivateKeyParameters)parameters;
                m_publicKey = null;

                m_random = m_deterministic ? null : CryptoServicesRegistrar.GetSecureRandom(providedRandom);
                m_engine = m_privateKey.Parameters.GetEngine();
            }
            else
            {
                m_privateKey = null;
                m_publicKey = (SlhDsaPublicKeyParameters)parameters;

                m_random = null;
                m_engine = m_publicKey.Parameters.GetEngine();
            }

            m_buffer.Init(context: providedContext ?? Array.Empty<byte>());
        }

        public void Update(byte input)
        {
            m_buffer.WriteByte(input);
        }

        public void BlockUpdate(byte[] input, int inOff, int inLen)
        {
            m_buffer.Write(input, inOff, inLen);
        }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public void BlockUpdate(ReadOnlySpan<byte> input)
        {
            m_buffer.Write(input);
        }
#endif

        public int GetMaxSignatureSize() => m_engine.SignatureLength;

        public byte[] GenerateSignature()
        {
            if (m_privateKey == null)
                throw new InvalidOperationException("SlhDsaSigner not initialised for signature generation.");

            return m_buffer.GenerateSignature(m_privateKey, m_engine, m_random);
        }

        public bool VerifySignature(byte[] signature)
        {
            if (m_publicKey == null)
                throw new InvalidOperationException("SlhDsaSigner not initialised for verification");

            return m_buffer.VerifySignature(m_publicKey, m_engine, signature);
        }

        public void Reset()
        {
            m_buffer.Reset();
        }

        private sealed class Buffer : MemoryStream
        {
            private int m_prefixLength;

            internal void Init(byte[] context)
            {
                lock (this)
                {
                    TruncateAndClear(newLength: 0);

                    // TODO Prehash variant uses 0x01 here
                    WriteByte(0x00);
                    WriteByte((byte)context.Length);
                    Write(context, 0, context.Length);

                    m_prefixLength = 2 + context.Length;
                }
            }

            internal byte[] GenerateSignature(SlhDsaPrivateKeyParameters privateKey, SphincsPlusEngine engine,
                SecureRandom random)
            {
                lock (this)
                {
                    byte[] buf = GetBuffer();
                    int count = Convert.ToInt32(Length);

                    byte[] addrnd = random == null ? null : SecureRandom.GetNextBytes(random, engine.N);

                    return privateKey.SignInternal(optRand: addrnd, buf, 0, count);
                }
            }

            internal bool VerifySignature(SlhDsaPublicKeyParameters publicKey, SphincsPlusEngine engine,
                byte[] signature)
            {
                if (engine.SignatureLength != signature.Length)
                {
                    Reset();
                    return false;
                }

                lock (this)
                {
                    byte[] buf = GetBuffer();
                    int count = Convert.ToInt32(Length);

                    return publicKey.VerifyInternal(buf, 0, count, signature);
                }
            }

            internal void Reset()
            {
                lock (this) TruncateAndClear(newLength: m_prefixLength);
            }

            private void TruncateAndClear(int newLength)
            {
                int count = Convert.ToInt32(Length);
                Array.Clear(GetBuffer(), newLength, count - newLength);
                SetLength(newLength);
            }
        }
    }
}
