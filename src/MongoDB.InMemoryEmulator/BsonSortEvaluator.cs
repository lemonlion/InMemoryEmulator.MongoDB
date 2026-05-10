using MongoDB.Bson;

namespace MongoDB.InMemoryEmulator;

/// <summary>
/// Evaluates MongoDB sort specifications against BsonDocument collections.
/// </summary>
/// <remarks>
/// Ref: https://www.mongodb.com/docs/manual/reference/method/cursor.sort/
///   "Specifies the order in which the query returns matching documents.
///    You must apply sort() to the cursor before retrieving any documents."
/// </remarks>
internal static class BsonSortEvaluator
{
    /// <summary>
    /// Sorts documents according to a rendered sort specification.
    /// </summary>
    /// <param name="docs">Documents to sort.</param>
    /// <param name="sort">Rendered sort document, e.g. { "date": -1, "name": 1 }.</param>
    /// <returns>Sorted documents.</returns>
    internal static IReadOnlyList<BsonDocument> Apply(IEnumerable<BsonDocument> docs, BsonDocument? sort)
    {
        if (sort == null || sort.ElementCount == 0)
            return docs as IReadOnlyList<BsonDocument> ?? docs.ToList();

        IOrderedEnumerable<BsonDocument>? ordered = null;

        foreach (var element in sort)
        {
            var field = element.Name;
            var direction = element.Value.AsInt32; // 1 = ascending, -1 = descending

            if (ordered == null)
            {
                ordered = direction == 1
                    ? docs.OrderBy(d => ResolveFieldForSort(d, field, direction), BsonValueComparer.Instance)
                    : docs.OrderByDescending(d => ResolveFieldForSort(d, field, direction), BsonValueComparer.Instance);
            }
            else
            {
                ordered = direction == 1
                    ? ordered.ThenBy(d => ResolveFieldForSort(d, field, direction), BsonValueComparer.Instance)
                    : ordered.ThenByDescending(d => ResolveFieldForSort(d, field, direction), BsonValueComparer.Instance);
            }
        }

        return ordered?.ToList() ?? docs.ToList();
    }

    /// <summary>
    /// Resolves a dot-notation field path to a BsonValue for sorting purposes.
    /// For array fields, extracts the minimum element (ascending) or maximum element (descending).
    /// Returns BsonNull for missing fields (sorts missing as lowest value).
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/reference/method/cursor.sort/#sort-asc-desc
    ///   "For an ascending sort, comparison of a multi-value field such as an array
    ///    to a single value field in another document, the sort picks the least value
    ///    of the multi-value field for comparison."
    ///   "For a descending sort, comparison treats the multi-value field as the greatest
    ///    value in the field."
    /// </remarks>
    private static BsonValue ResolveFieldForSort(BsonDocument doc, string path, int direction)
    {
        var value = BsonFilterEvaluator.ResolveFieldPath(doc, path);

        if (value is BsonArray array && array.Count > 0)
        {
            return direction == 1
                ? array.OrderBy(x => x, BsonValueComparer.Instance).First()
                : array.OrderByDescending(x => x, BsonValueComparer.Instance).First();
        }

        return value;
    }
}
