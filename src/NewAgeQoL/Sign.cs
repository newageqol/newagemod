using System;
using System.Security.Cryptography;

namespace NewAgeQoL
{
    internal static class Sign
    {
        private const string Modulus = "uFt4WHzIefktgxJDaE8RmKSCNDBnalz9jG6Y3sd22wLRY+LMnEOPY/zVfRxXYSHzL2YoEkmtK88nXnZ8I4H+Jo6Witp5TGY/RfWs/Kkg1JV9Yqdt0h5GnkjZKHT4dDMwEKgfHhAEjpObsWH6s/roPSrgdjoPve2IJizzinNE88xxjDrXdXsTOy9O54/uOnWWj6lsiLOoevcNOLFyPnQa+hTijcXjDDOZMlH4qaBiXWniekkbcaWDICwQqRjTDCzU3FRrbPo+rM2p/z7sgx97F4Wu8+IBldk7JJhQKpLkXy2pq4JWwcqgk8gsksJ3YMtMkZtVHh02tMxedV0dIPerVQ==";
        private const string Exponent = "AQAB";

        internal static bool Valid(byte[] data, byte[] signature)
        {
            if (data == null || signature == null || signature.Length == 0) return false;
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                using (var sha = SHA256.Create())
                {
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(Modulus),
                        Exponent = Convert.FromBase64String(Exponent),
                    });
                    var check = new RSAPKCS1SignatureDeformatter(rsa);
                    check.SetHashAlgorithm("SHA256");
                    return check.VerifySignature(sha.ComputeHash(data), signature);
                }
            }
            catch (Exception e)
            {
                Plugin.Trace("[sign] " + e.Message);
                return false;
            }
        }
    }
}
