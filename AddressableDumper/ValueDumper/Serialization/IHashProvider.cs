using System;

namespace AddressableDumper.ValueDumper.Serialization
{
    public interface IHashProvider
    {
        public byte[] ComputeHash(object value);
    }
}
