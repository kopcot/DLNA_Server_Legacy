using DLNAServer.Helpers.Attributes;
using DLNAServer.Helpers.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using System.Reflection;

namespace DLNAServer.Database.Entities.Configurations
{
    public sealed class BaseEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity> where TEntity : BaseEntity
    {
        public void Configure(EntityTypeBuilder<TEntity> builder)
        {
            ArgumentNullException.ThrowIfNull(nameof(builder));

            _ = builder.HasKey(static (e) => e.Id);

            // Configure Sequential Guid for Id
            _ = builder.Property(static (e) => e.Id)
                .IsRequired(true)
                .HasValueGenerator<SequentialGuidValueGenerator>()
                .ValueGeneratedOnAdd();
            _ = builder.Property(static (e) => e.CreatedInDB)
                .IsRequired(true)
                .ValueGeneratedOnAdd();
            _ = builder.Property(static (e) => e.ModifiedInDB)
                .IsRequired(false)
                .ValueGeneratedOnUpdate();

            // Intern string names, for less 
            var entityType = typeof(TEntity);
            _ = string.Intern(entityType.Name);
            _ = string.Intern(entityType.FullName ?? string.Empty);
            _ = string.Intern(entityType.Namespace ?? string.Empty);
            var properties = entityType.GetProperties();
            foreach (var property in properties)
            {
                _ = string.Intern(property.Name);
            }

            // Configure properties of the entity
            foreach (var property in properties)
            {
                var propertyName = string.Intern(property.Name);
                {
                    // Check if the property has the LowercaseAttribute
                    var lowercaseAttribute = property.GetCustomAttribute<LowercaseAttribute>();
                    if (lowercaseAttribute != null
                        && lowercaseAttribute.PropertyName is string lowecasePropertyName
                        && properties.Any(p => p.Name == lowecasePropertyName))
                    {
                        // If the attribute is present, apply the computed column logic to convert it to lowercase
                        _ = builder.Property(propertyName)
                            //.HasComputedColumnSql($"LOWER([{lowercaseAttribute.PropertyName}])", stored: true)
                            .HasComputedColumnSql($"LOWER(`{lowecasePropertyName}`)", stored: true)
                            .ValueGeneratedOnAddOrUpdate();
                    }

                    // Check if the property has the InternStringAttribute
                    var internStringAttribute = property.GetCustomAttribute<InternStringAttribute>();
                    if (internStringAttribute != null
                        && (property.PropertyType == typeof(string)))
                    {
                        _ = builder.Property(propertyName)
                            .HasConversion(_internStringConverter);
                    }
                    
                    // Check if the property has the StringCacheAttribute
                    var cacheStringAttribute = property.GetCustomAttribute<StringCacheAttribute>();
                    if (cacheStringAttribute != null
                        && (property.PropertyType == typeof(string)))
                    {
                        _ = builder.Property(propertyName)
                            .HasConversion(_stringCacheConverter);
                    }
                }
            }
        }
        private static readonly InternStringConverter _internStringConverter = new();
        private static readonly StringCacheConverter _stringCacheConverter = new();
    }
}
