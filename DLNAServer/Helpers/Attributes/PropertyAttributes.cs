namespace DLNAServer.Helpers.Attributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class LowercaseAttribute : Attribute
    {
        public string PropertyName { get; }
        public LowercaseAttribute(string propertyName)
        {
            PropertyName = propertyName;
        }
    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class InternStringAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class StringCacheAttribute : Attribute
    {
    }
}
