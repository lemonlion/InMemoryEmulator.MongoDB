using MongoDB.Bson;

namespace MongoDB.InMemoryEmulator;

/// <summary>
/// Evaluates MongoDB projection specifications against BsonDocuments.
/// </summary>
/// <remarks>
/// Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/
///   "Projections determine which fields are returned in the matching documents."
///   Inclusion and exclusion cannot be mixed (except _id: 0).
///   _id is included by default unless explicitly excluded.
/// </remarks>
internal static class BsonProjectionEvaluator
{
    /// <summary>
    /// Applies a projection to a document.
    /// </summary>
    /// <param name="doc">The source document.</param>
    /// <param name="projection">Rendered projection, e.g. { name: 1, _id: 0 } or { password: 0 }.</param>
    /// <returns>The projected document.</returns>
    internal static BsonDocument Apply(BsonDocument doc, BsonDocument? projection)
    {
        if (projection == null || projection.ElementCount == 0)
            return doc;

        var mode = DetermineMode(projection);

        if (mode == ProjectionMode.Inclusion)
            return ApplyInclusion(doc, projection);
        else
            return ApplyExclusion(doc, projection);
    }

    private enum ProjectionMode
    {
        Inclusion,
        Exclusion
    }

    /// <summary>
    /// Determines whether the projection is inclusion or exclusion.
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.find/#std-label-find-projection
    ///   "You cannot combine inclusion and exclusion statements, with the exception of the _id field."
    /// </remarks>
    private static ProjectionMode DetermineMode(BsonDocument projection)
    {
        foreach (var element in projection)
        {
            if (element.Name == "_id") continue;

            if (element.Value.IsBsonDocument)
            {
                // Projection operators like $elemMatch, $slice, $meta are inclusion mode
                return ProjectionMode.Inclusion;
            }

            var val = element.Value.IsNumeric ? element.Value.ToInt32() : (element.Value.AsBoolean ? 1 : 0);
            return val == 1 ? ProjectionMode.Inclusion : ProjectionMode.Exclusion;
        }

        // Only _id projection — treat as exclusion (or inclusion of _id only)
        if (projection.Contains("_id"))
        {
            var idVal = projection["_id"].IsNumeric ? projection["_id"].ToInt32() : (projection["_id"].AsBoolean ? 1 : 0);
            return idVal == 0 ? ProjectionMode.Exclusion : ProjectionMode.Inclusion;
        }

        return ProjectionMode.Inclusion;
    }

    /// <summary>
    /// In inclusion mode, only listed fields are included. _id is included by default.
    /// </summary>
    private static BsonDocument ApplyInclusion(BsonDocument doc, BsonDocument projection)
    {
        var result = new BsonDocument();

        // _id handling: included by default unless explicitly excluded
        bool includeId = true;
        if (projection.Contains("_id"))
        {
            var idVal = projection["_id"];
            includeId = idVal.IsNumeric ? idVal.ToInt32() != 0 : idVal.AsBoolean;
        }

        if (includeId && doc.Contains("_id"))
            result["_id"] = doc["_id"];

        foreach (var element in projection)
        {
            if (element.Name == "_id") continue;

            if (element.Value.IsBsonDocument)
            {
                // Handle projection operators ($slice, $elemMatch, $meta)
                ApplyProjectionOperator(doc, result, element.Name, element.Value.AsBsonDocument);
                continue;
            }

            var val = element.Value.IsNumeric ? element.Value.ToInt32() : (element.Value.AsBoolean ? 1 : 0);
            if (val == 1)
            {
                CopyField(doc, result, element.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// In exclusion mode, all fields are included except listed ones.
    /// Supports dot-notation for nested field exclusion.
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/tutorial/project-fields-from-query-results/
    ///   "Use dot notation to suppress fields in embedded documents."
    /// </remarks>
    private static BsonDocument ApplyExclusion(BsonDocument doc, BsonDocument projection)
    {
        var excludedFields = new HashSet<string>();
        foreach (var element in projection)
        {
            var val = element.Value.IsNumeric ? element.Value.ToInt32() : (element.Value.AsBoolean ? 1 : 0);
            if (val == 0)
                excludedFields.Add(element.Name);
        }

        // Separate top-level exclusions from dot-notation exclusions
        var topLevel = new HashSet<string>();
        var dotNotation = new List<string>();
        foreach (var field in excludedFields)
        {
            if (field.Contains('.'))
                dotNotation.Add(field);
            else
                topLevel.Add(field);
        }

        var result = new BsonDocument();
        foreach (var element in doc)
        {
            if (topLevel.Contains(element.Name))
                continue;
            result[element.Name] = element.Value;
        }

        // Apply dot-notation exclusions to nested fields
        foreach (var field in dotNotation)
        {
            RemoveNestedField(result, field);
        }

        return result;
    }

    /// <summary>
    /// Removes a nested field specified by dot-notation path.
    /// </summary>
    private static void RemoveNestedField(BsonDocument doc, string dotPath)
    {
        var parts = dotPath.Split('.');
        var current = doc;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.Contains(parts[i]) || !current[parts[i]].IsBsonDocument)
                return;
            current = current[parts[i]].AsBsonDocument;
        }
        current.Remove(parts[^1]);
    }

    /// <summary>
    /// Copies a field (including dot-notation nested paths) from source to target.
    /// </summary>
    private static void CopyField(BsonDocument source, BsonDocument target, string fieldPath)
    {
        if (!fieldPath.Contains('.'))
        {
            if (source.Contains(fieldPath))
                target[fieldPath] = source[fieldPath];
            return;
        }

        // Dot notation: "address.city" → copy nested path
        var parts = fieldPath.Split('.');
        var current = source;
        var targetCurrent = target;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.Contains(parts[i]) || !current[parts[i]].IsBsonDocument)
                return;

            if (!targetCurrent.Contains(parts[i]) || !targetCurrent[parts[i]].IsBsonDocument)
                targetCurrent[parts[i]] = new BsonDocument();

            current = current[parts[i]].AsBsonDocument;
            targetCurrent = targetCurrent[parts[i]].AsBsonDocument;
        }

        var lastPart = parts[^1];
        if (current.Contains(lastPart))
            targetCurrent[lastPart] = current[lastPart];
    }

    /// <summary>
    /// Handles projection operators ($slice, $elemMatch, $meta).
    /// </summary>
    private static void ApplyProjectionOperator(BsonDocument doc, BsonDocument result, string field, BsonDocument operatorDoc)
    {
        if (operatorDoc.Contains("$slice"))
        {
            ApplySlice(doc, result, field, operatorDoc["$slice"]);
        }
        else if (operatorDoc.Contains("$elemMatch"))
        {
            ApplyElemMatch(doc, result, field, operatorDoc["$elemMatch"].AsBsonDocument);
        }
        else if (operatorDoc.Contains("$meta"))
        {
            // $meta: "textScore" — stub: include field with 0.0 score
            result[field] = 0.0;
        }
        else
        {
            // Unknown operator — just include the field
            if (doc.Contains(field))
                result[field] = doc[field];
        }
    }

    /// <summary>
    /// $slice projection: returns a subset of an array.
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/slice/
    ///   { field: { $slice: N } } — first N elements (positive) or last N (negative)
    ///   { field: { $slice: [skip, limit] } } — skip elements, then take limit
    /// </remarks>
    private static void ApplySlice(BsonDocument doc, BsonDocument result, string field, BsonValue sliceSpec)
    {
        if (!doc.Contains(field) || !doc[field].IsBsonArray)
        {
            if (doc.Contains(field))
                result[field] = doc[field];
            return;
        }

        var array = doc[field].AsBsonArray;

        if (sliceSpec.IsNumeric)
        {
            var n = sliceSpec.ToInt32();
            if (n >= 0)
                result[field] = new BsonArray(array.Take(n));
            else
                result[field] = new BsonArray(array.Skip(Math.Max(0, array.Count + n)));
        }
        else if (sliceSpec.IsBsonArray)
        {
            var arr = sliceSpec.AsBsonArray;
            var skip = arr[0].ToInt32();
            var limit = arr[1].ToInt32();
            if (skip < 0)
                skip = Math.Max(0, array.Count + skip);
            result[field] = new BsonArray(array.Skip(skip).Take(limit));
        }
    }

    /// <summary>
    /// $elemMatch projection: returns only the first matching array element.
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/elemMatch/
    ///   "Projects the first element in an array that matches the specified $elemMatch condition."
    /// </remarks>
    private static void ApplyElemMatch(BsonDocument doc, BsonDocument result, string field, BsonDocument filter)
    {
        if (!doc.Contains(field) || !doc[field].IsBsonArray)
            return;

        var array = doc[field].AsBsonArray;
        foreach (var element in array)
        {
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/elemMatch/
            //   "Projects the first element in an array that matches the specified $elemMatch condition."
            //   Works with both document and scalar array elements.
            if (element.IsBsonDocument)
            {
                if (BsonFilterEvaluator.Matches(element.AsBsonDocument, filter))
                {
                    result[field] = new BsonArray { element };
                    return;
                }
            }
            else
            {
                // For scalar elements, wrap in a temporary document and match against the filter
                // treating the filter operators as applying to the element value directly.
                var wrapper = new BsonDocument("_v", element);
                var wrappedFilter = new BsonDocument("_v", filter);
                if (BsonFilterEvaluator.Matches(wrapper, wrappedFilter))
                {
                    result[field] = new BsonArray { element };
                    return;
                }
            }
        }

        // No match — field not included
    }
}
