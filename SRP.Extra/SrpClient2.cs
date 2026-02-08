//using System.Buffers;
//using System.Security;
//using System.Security.Cryptography;
//using SecureRemotePassword;

//namespace SRP.Extra;

//public class SrpClient2<THashFunction> : ISrpClient where THashFunction : HashAlgorithm
//{
//    //
//    // Сводка:
//    //     Gets or sets the protocol parameters.
//    private readonly SrpParameters _parameters;
//    private readonly THashFunction _hashFunction;
//    private readonly BigInteger _prime;

//    public int SaltSize => _parameters.HashSizeBytes;

//    public SrpClient2(THashFunction hashFunction)
//    {
//        _parameters = new SrpParameters();
//        _prime = _parameters.Prime.ToByteArray();
//        _hashFunction = hashFunction;
//    }

//    public SrpClient2(THashFunction hashFunction, SrpParameters parameters)
//    {
//        ArgumentNullException.ThrowIfNull(parameters, nameof(parameters));
//        ArgumentNullException.ThrowIfNull(hashFunction, nameof(hashFunction));
//        _parameters = parameters;
//        _hashFunction = hashFunction;
//    }

//    public void GenerateSalt(Span<byte> salt)
//    {
//        if (salt.Length != SaltSize)
//        {
//            throw new ArgumentOutOfRangeException(nameof(salt));
//        }

//        RandomNumberGenerator.Fill(salt);

//        // Make positive
//        ref byte mostSignificantByte = ref salt[salt.Length - 1];
//        mostSignificantByte = (byte)(mostSignificantByte & ~0x80);
//    }

//    private void Hash(Span<byte> dest, ReadOnlySpan<byte> source)
//    {
//        if (!_hashFunction.TryComputeHash(source, dest, out int written) || written != _hashFunction.HashSize)
//        {
//            throw new InvalidOperationException();
//        }
//    }

//    private void Hash(Span<byte> dest, ReadOnlySpan<byte> sourceA, ReadOnlySpan<byte> sourceB)
//    {
//        var buffer = ArrayPool<byte>.Shared.Rent(sourceA.Length + sourceB.Length);
//        try
//        {
//            sourceA.CopyTo(buffer);
//            sourceB.CopyTo(buffer.AsSpan().Slice(sourceA.Length));
//            Hash(dest, buffer.AsSpan(0, sourceA.Length + sourceB.Length));
//        }
//        finally
//        {
//            ArrayPool<byte>.Shared.Return(buffer);
//        }
//    }

//    private void Hash(Span<byte> dest, ReadOnlySpan<byte> sourceA, ReadOnlySpan<byte> sourceB, ReadOnlySpan<byte> sourceC)
//    {
//        int totalLen = sourceA.Length + sourceB.Length + sourceC.Length;
//        var buffer = ArrayPool<byte>.Shared.Rent(totalLen);
//        try
//        {
//            sourceA.CopyTo(buffer);
//            sourceB.CopyTo(buffer.AsSpan().Slice(sourceA.Length));
//            sourceC.CopyTo(buffer.AsSpan().Slice(sourceA.Length + sourceB.Length));
//            Hash(dest, buffer.AsSpan(0, totalLen));
//        }
//        finally
//        {
//            ArrayPool<byte>.Shared.Return(buffer);
//        }
//    }

//    public void DerivePrivateKey(Span<byte> privateKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> username, ReadOnlySpan<byte> password)
//    {
//        if (privateKey.Length != _hashFunction.HashSize)
//        {
//            throw new Exception("resultHash has wrong size");
//        }

//        Hash(privateKey, username, ":"u8, password);
//        Hash(privateKey, salt, privateKey);
//    }

//    //
//    // Сводка:
//    //     Derives the verifier from the private key.
//    //
//    // Параметры:
//    //   privateKey:
//    //     The private key.
//    public BigInteger DeriveVerifier(ReadOnlySpan<byte> privateKey)
//    {
//        BigInteger prime = _parameters.Prime;
//        BigInteger generator = _parameters.Generator;
//        BigInteger exponent = new BigInteger(privateKey);
//        return BigInteger.ModPow(generator, exponent, prime);
//    }

//    //
//    // Сводка:
//    //     Generates the ephemeral value.
//    public SrpEphemeral GenerateEphemeral()
//    {
//        SrpInteger srpInteger = SrpInteger.RandomInteger(_parameters.HashSizeBytes);
//        SrpInteger srpInteger2 = ComputeA(srpInteger);
//        return new SrpEphemeral
//        {
//            Secret = srpInteger.ToHex(),
//            Public = srpInteger2.ToHex()
//        };
//    }

//    //
//    // Сводка:
//    //     Computes the public ephemeral value using the specified secret.
//    //
//    // Параметры:
//    //   a:
//    //     Secret ephemeral value.
//    internal SrpInteger ComputeA(SrpInteger a)
//    {
//        SrpInteger prime = _parameters.Prime;
//        return _parameters.Generator.ModPow(a, prime);
//    }

//    //
//    // Сводка:
//    //     Computes the value of u = H(PAD(A), PAD(B)).
//    //
//    // Параметры:
//    //   A:
//    //     Client public ehemeral value.
//    //
//    //   B:
//    //     Server public ehemeral value.
//    internal SrpInteger ComputeU(SrpInteger A, SrpInteger B)
//    {
//        SrpParameters.SrpHashFunction hash = _parameters.Hash;
//        Func<SrpInteger, SrpInteger> pad = _parameters.Pad;
//        return hash(pad(A), pad(B));
//    }

//    //
//    // Сводка:
//    //     Computes S, the premaster-secret.
//    //
//    // Параметры:
//    //   a:
//    //     Client secret ephemeral value.
//    //
//    //   B:
//    //     Server public ephemeral value.
//    //
//    //   u:
//    //     The computed value of u.
//    //
//    //   x:
//    //     The private key.
//    internal SrpInteger ComputeS(SrpInteger a, SrpInteger B, SrpInteger u, SrpInteger x)
//    {
//        SrpInteger prime = _parameters.Prime;
//        SrpInteger generator = _parameters.Generator;
//        SrpInteger multiplier = _parameters.Multiplier;
//        return (B - multiplier * generator.ModPow(x, prime)).ModPow(a + u * x, prime);
//    }

//    //
//    // Сводка:
//    //     Derives the client session.
//    //
//    // Параметры:
//    //   clientSecretEphemeral:
//    //     The client secret ephemeral.
//    //
//    //   serverPublicEphemeral:
//    //     The server public ephemeral.
//    //
//    //   salt:
//    //     The salt.
//    //
//    //   username:
//    //     The username.
//    //
//    //   privateKey:
//    //     The private key.
//    //
//    // Возврат:
//    //     Session key and proof.
//    public SrpSession DeriveSession(string clientSecretEphemeral, string serverPublicEphemeral, string salt, string username, string privateKey)
//    {
//        SrpInteger prime = _parameters.Prime;
//        SrpInteger generator = _parameters.Generator;
//        SrpParameters.SrpHashFunction hash = _parameters.Hash;
//        SrpInteger srpInteger = SrpInteger.FromHex(clientSecretEphemeral);
//        SrpInteger srpInteger2 = SrpInteger.FromHex(serverPublicEphemeral);
//        SrpInteger srpInteger3 = SrpInteger.FromHex(salt);
//        string text = username + string.Empty;
//        SrpInteger x = SrpInteger.FromHex(privateKey);
//        SrpInteger srpInteger4 = generator.ModPow(srpInteger, prime);
//        if (srpInteger2 % prime == 0)
//        {
//            throw new SecurityException("The server sent an invalid public ephemeral");
//        }

//        SrpInteger u = ComputeU(srpInteger4, srpInteger2);
//        SrpInteger srpInteger5 = ComputeS(srpInteger, srpInteger2, u, x);
//        SrpInteger srpInteger6 = hash(srpInteger5);
//        SrpInteger srpInteger7 = hash(hash(prime) ^ hash(generator), hash(text), srpInteger3, srpInteger4, srpInteger2, srpInteger6);
//        return new SrpSession
//        {
//            Key = srpInteger6.ToHex(),
//            Proof = srpInteger7.ToHex()
//        };
//    }

//    //
//    // Сводка:
//    //     Verifies the session using the server-provided session proof.
//    //
//    // Параметры:
//    //   clientPublicEphemeral:
//    //     The client public ephemeral.
//    //
//    //   clientSession:
//    //     The client session.
//    //
//    //   serverSessionProof:
//    //     The server session proof.
//    public void VerifySession(string clientPublicEphemeral, SrpSession clientSession, string serverSessionProof)
//    {
//        SrpParameters.SrpHashFunction hash = _parameters.Hash;
//        SrpInteger srpInteger = SrpInteger.FromHex(clientPublicEphemeral);
//        SrpInteger srpInteger2 = SrpInteger.FromHex(clientSession.Proof);
//        SrpInteger srpInteger3 = SrpInteger.FromHex(clientSession.Key);
//        SrpInteger srpInteger4 = hash(srpInteger, srpInteger2, srpInteger3);
//        if (SrpInteger.FromHex(serverSessionProof) != srpInteger4)
//        {
//            throw new SecurityException("Server provided session proof is invalid");
//        }
//    }
//}