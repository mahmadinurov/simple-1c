using System;
using System.Collections.Generic;
using System.Linq;
using Simple1C.Impl.Helpers;
using Simple1C.Impl.Sql.SchemaMapping;
using Simple1C.Impl.Sql.SqlAccess.Syntax;
using Simple1C.Interface;

namespace Simple1C.Impl.Sql.Translation.QueryEntities
{
    internal class QueryEntityAccessor
    {
        private readonly QueryEntityRegistry queryEntityRegistry;
        private readonly NameGenerator nameGenerator = new NameGenerator();

        public QueryEntityAccessor(QueryEntityRegistry queryEntityRegistry)
        {
            this.queryEntityRegistry = queryEntityRegistry;
        }

        public QueryField GetOrCreateQueryField(TableDeclarationClause declaration,
            string[] propertyNames, bool isRepresentation, SelectPart selectPart)
        {
            var queryRoot = queryEntityRegistry.Get(declaration);
            var keyWithoutFunction = string.Join(".", propertyNames);
            if (!isRepresentation && selectPart == SelectPart.GroupBy)
            {
                QueryField fieldWithFunction;
                var keyWithFunction = keyWithoutFunction + "." + true;
                if (queryRoot.fields.TryGetValue(keyWithFunction, out fieldWithFunction))
                    if (fieldWithFunction.parts.Contains(SelectPart.Select))
                        isRepresentation = true;
            }
            var key = keyWithoutFunction + "." + isRepresentation;
            QueryField field;
            if (!queryRoot.fields.TryGetValue(key, out field))
            {
                var subqueryRequired = propertyNames.Length > 1;
                var needInvert = false;
                if (propertyNames[propertyNames.Length - 1] == "ЭтоГруппа")
                {
                    needInvert = true;
                    subqueryRequired = true;
                }
                var propertiesEnumerator = new PropertiesEnumerator(propertyNames, queryRoot, this);
                var referencedProperties = propertiesEnumerator.Enumerate();
                if (referencedProperties.Count == 0)
                    throw new InvalidOperationException("assertion failure");
                if (isRepresentation)
                    if (ReplaceWithRepresentation(referencedProperties))
                        subqueryRequired = true;
                string fieldAlias = null;
                if (subqueryRequired)
                {
                    queryRoot.subqueryRequired = true;
                    fieldAlias = nameGenerator.GenerateColumnName();
                }
                foreach (var p in referencedProperties)
                    p.referenced = true;
                field = new QueryField(fieldAlias, referencedProperties.ToArray(), needInvert);
                queryRoot.fields.Add(key, field);
            }
            if (!field.parts.Contains(selectPart))
                field.parts.Add(selectPart);
            return field;
        }

        public QueryEntityProperty GetOrCreatePropertyIfExists(QueryEntity queryEntity, string name)
        {
            foreach (var f in queryEntity.properties)
                if (f.mapping.PropertyName.EqualsIgnoringCase(name))
                    return f;
            if (!queryEntity.mapping.HasProperty(name))
                return null;
            var propertyMapping = queryEntity.mapping.GetByPropertyName(name);
            var property = new QueryEntityProperty(queryEntity, propertyMapping);
            if (propertyMapping.SingleLayout != null)
            {
                if (name == "Ссылка")
                {
                    if (queryEntity.mapping.Type == TableType.TableSection)
                    {
                        var nestedTableName = queryEntity.mapping.QueryTableName;
                        nestedTableName = TableMapping.GetMainQueryNameByTableSectionQueryName(nestedTableName);
                        AddQueryEntity(property, nestedTableName);
                    }
                    else
                        property.nestedEntities.Add(queryEntity);
                }
                else
                {
                    var nestedTableName = propertyMapping.SingleLayout.NestedTableName;
                    if (!string.IsNullOrEmpty(nestedTableName))
                        AddQueryEntity(property, nestedTableName);
                }
            }
            else
                foreach (var t in propertyMapping.UnionLayout.NestedTables)
                    AddQueryEntity(property, t);
            queryEntity.properties.Add(property);
            return property;
        }

        public TableDeclarationClause GetTableDeclaration(QueryEntity entity)
        {
            return entity.declaration
                   ?? (entity.declaration = new TableDeclarationClause
                   {
                       Name = entity.mapping.DbTableName,
                       Alias = nameGenerator.GenerateTableName()
                   });
        }

        public JoinClause CreateEnumMappingsJoinClause(QueryEntity enumEntity)
        {
            if (!enumEntity.mapping.ObjectName.HasValue)
                throw new InvalidOperationException("assertion failure");
            var declaration = new TableDeclarationClause
            {
                Name = "simple1c__enumMappings",
                Alias = nameGenerator.GenerateTableName()
            };
            return new JoinClause
            {
                Source = declaration,
                JoinKind = JoinKind.Left,
                Condition = new AndExpression
                {
                    Left = new EqualityExpression
                    {
                        Left = new ColumnReferenceExpression
                        {
                            Name = "enumName",
                            Declaration = declaration
                        },
                        Right = new LiteralExpression
                        {
                            Value = enumEntity.mapping.ObjectName.Value.Name
                        }
                    },
                    Right = new EqualityExpression
                    {
                        Left = new ColumnReferenceExpression
                        {
                            Name = "orderIndex",
                            Declaration = declaration
                        },
                        Right = new ColumnReferenceExpression
                        {
                            Name = enumEntity.GetSingleColumnName("Порядок"),
                            Declaration = GetTableDeclaration(enumEntity)
                        }
                    }
                }
            };
        }

        private bool ReplaceWithRepresentation(List<QueryEntityProperty> properties)
        {
            var result = false;
            for (var i = properties.Count - 1; i >= 0; i--)
            {
                var property = properties[i];
                if (property.nestedEntities.Count == 0)
                    continue;
                properties.RemoveAt(i);
                foreach (var nestedEntity in property.nestedEntities)
                {
                    var scope = nestedEntity.mapping.ObjectName.HasValue
                        ? nestedEntity.mapping.ObjectName.Value.Scope
                        : (ConfigurationScope?) null;
                    var validScopes = new ConfigurationScope?[]
                    {
                        ConfigurationScope.Перечисления, ConfigurationScope.Справочники
                    };
                    if (!validScopes.Contains(scope))
                    {
                        const string messageFormat = "[ПРЕДСТАВЛЕНИЕ] is only supported for [{0}]";
                        throw new InvalidOperationException(string.Format(messageFormat, validScopes.JoinStrings(",")));
                    }
                    var propertyName = scope == ConfigurationScope.Справочники ? "Наименование" : "Порядок";
                    var presentationProperty = GetOrCreatePropertyIfExists(nestedEntity, propertyName);
                    if (presentationProperty == null)
                    {
                        const string messageFormat = "entity [{0}] has no property [{1}]";
                        throw new InvalidOperationException(string.Format(messageFormat,
                            nestedEntity.mapping.QueryTableName, propertyName));
                    }
                    properties.Add(presentationProperty);
                    result = true;
                }
            }
            return result;
        }

        private void AddQueryEntity(QueryEntityProperty referer, string tableName)
        {
            var queryEntity = queryEntityRegistry.CreateQueryEntity(referer, tableName);
            referer.nestedEntities.Add(queryEntity);
        }
    }
}