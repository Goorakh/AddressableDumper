namespace AddressableDumper.ValueDumper.Serialization
{
    public enum WriteOperationType
    {
        None,
        Null,
        Comment,
        Value,
        ValueRaw,
        StartArray,
        EndArray,
        StartObject,
        EndObject,
        PropertyName,
    }
}
