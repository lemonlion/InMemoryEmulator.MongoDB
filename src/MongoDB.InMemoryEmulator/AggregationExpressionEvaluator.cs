using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace MongoDB.InMemoryEmulator;

/// <summary>
/// Evaluates MongoDB aggregation expressions ($add, $concat, $cond, field references, etc.).
/// Used by $project, $addFields, $group accumulators, $match ($expr), and pipeline updates.
/// </summary>
/// <remarks>
/// Ref: https://www.mongodb.com/docs/manual/meta/aggregation-quick-reference/#expressions
///   "Expressions can include field paths, literals, system variables, expression objects, and expression operators."
/// </remarks>
internal static class AggregationExpressionEvaluator
{
    /// <summary>
    /// Sentinel value for $$REMOVE — indicates the field should be excluded from the output.
    /// Distinguished from BsonNull.Value because null fields are included, but $$REMOVE fields are omitted.
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/reference/aggregation-variables/
    ///   "$$REMOVE evaluates to the missing value. Allows for the exclusion of fields
    ///    in $addFields and $project stages."
    /// </remarks>
    internal static readonly BsonValue RemoveSentinel = new BsonString("$$__REMOVE_SENTINEL__$$");

    /// <summary>
    /// Returns true if the value is the $$REMOVE sentinel.
    /// </summary>
    internal static bool IsRemove(BsonValue value) => ReferenceEquals(value, RemoveSentinel);

    /// <summary>
    /// Evaluate an expression against a document, returning the resulting BsonValue.
    /// </summary>
    internal static BsonValue Evaluate(BsonDocument doc, BsonValue expr, BsonDocument? variables = null)
    {
        // Ref: https://www.mongodb.com/docs/manual/meta/aggregation-quick-reference/#expressions
        //   "Field path: $fieldName or $nested.field"
        if (expr is BsonString s)
        {
            if (s.Value.StartsWith("$$"))
            {
                var varName = s.Value[2..];
                return ResolveVariable(varName, doc, variables);
            }
            if (s.Value.StartsWith("$"))
            {
                var fieldPath = s.Value[1..];
                return BsonFilterEvaluator.ResolveFieldPath(doc, fieldPath);
            }
            return expr; // literal string
        }

        // Literal values pass through
        if (expr is not BsonDocument exprDoc)
            return expr;

        // Empty doc is literal
        if (exprDoc.ElementCount == 0)
            return exprDoc;

        // Check if it's an operator expression (single key starting with $)
        var firstName = exprDoc.GetElement(0).Name;
        if (!firstName.StartsWith("$"))
            return exprDoc; // literal document

        if (exprDoc.ElementCount == 1)
        {
            var args = exprDoc[firstName];
            return EvaluateOperator(doc, firstName, args, variables);
        }

        // Multi-key document with $-keys could be $cond shorthand or literal
        // $cond: { if: ..., then: ..., else: ... }
        if (exprDoc.Contains("$cond"))
            return EvaluateOperator(doc, "$cond", exprDoc["$cond"], variables);

        return exprDoc;
    }

    private static BsonValue EvaluateOperator(BsonDocument doc, string op, BsonValue args, BsonDocument? variables)
    {
        return op switch
        {
            // Arithmetic
            "$add" => EvalAdd(doc, args, variables),
            "$subtract" => EvalSubtract(doc, args, variables),
            "$multiply" => EvalMultiply(doc, args, variables),
            "$divide" => EvalDivide(doc, args, variables),
            "$mod" => EvalMod(doc, args, variables),
            "$abs" => EvalUnaryMath(doc, args, variables, Math.Abs),
            "$ceil" => EvalUnaryMath(doc, args, variables, Math.Ceiling),
            "$floor" => EvalUnaryMath(doc, args, variables, Math.Floor),
            "$round" => EvalRound(doc, args, variables),
            "$trunc" => EvalTrunc(doc, args, variables),
            "$pow" => EvalBinaryMath(doc, args, variables, Math.Pow),
            "$sqrt" => EvalUnaryMath(doc, args, variables, Math.Sqrt),
            "$log" => EvalBinaryMath(doc, args, variables, Math.Log),
            "$log10" => EvalUnaryMath(doc, args, variables, Math.Log10),
            "$ln" => EvalUnaryMath(doc, args, variables, Math.Log),
            "$exp" => EvalUnaryMath(doc, args, variables, Math.Exp),

            // Trig
            "$sin" => EvalUnaryMath(doc, args, variables, Math.Sin),
            "$cos" => EvalUnaryMath(doc, args, variables, Math.Cos),
            "$tan" => EvalUnaryMath(doc, args, variables, Math.Tan),
            "$asin" => EvalUnaryMath(doc, args, variables, Math.Asin),
            "$acos" => EvalUnaryMath(doc, args, variables, Math.Acos),
            "$atan" => EvalUnaryMath(doc, args, variables, Math.Atan),
            "$atan2" => EvalBinaryMath(doc, args, variables, Math.Atan2),
            "$degreesToRadians" => EvalUnaryMath(doc, args, variables, d => d * Math.PI / 180.0),
            "$radiansToDegrees" => EvalUnaryMath(doc, args, variables, d => d * 180.0 / Math.PI),

            // String
            "$concat" => EvalConcat(doc, args, variables),
            "$toLower" => EvalStringUnary(doc, args, variables, s => s.ToLowerInvariant()),
            "$toUpper" => EvalStringUnary(doc, args, variables, s => s.ToUpperInvariant()),
            "$trim" => EvalTrim(doc, args, variables, s => s.Trim(), (s, c) => s.Trim(c)),
            "$ltrim" => EvalTrim(doc, args, variables, s => s.TrimStart(), (s, c) => s.TrimStart(c)),
            "$rtrim" => EvalTrim(doc, args, variables, s => s.TrimEnd(), (s, c) => s.TrimEnd(c)),
            "$substr" or "$substrBytes" => EvalSubstr(doc, args, variables),
            "$substrCP" => EvalSubstr(doc, args, variables),
            "$strLenBytes" => EvalStrLen(doc, args, variables, s => Encoding.UTF8.GetByteCount(s)),
            "$strLenCP" => EvalStrLen(doc, args, variables, s => s.Length),
            "$indexOfBytes" or "$indexOfCP" => EvalIndexOf(doc, args, variables),
            "$split" => EvalSplit(doc, args, variables),
            "$strcasecmp" => EvalStrcasecmp(doc, args, variables),
            "$replaceOne" => EvalReplaceOne(doc, args, variables),
            "$replaceAll" => EvalReplaceAll(doc, args, variables),
            "$regexMatch" => EvalRegexMatch(doc, args, variables),
            "$regexFind" => EvalRegexFind(doc, args, variables),
            "$regexFindAll" => EvalRegexFindAll(doc, args, variables),

            // Comparison
            "$cmp" => EvalCmp(doc, args, variables),
            "$eq" => EvalCompare(doc, args, variables, r => r == 0),
            "$ne" => EvalCompare(doc, args, variables, r => r != 0),
            "$gt" => EvalCompare(doc, args, variables, r => r > 0),
            "$gte" => EvalCompare(doc, args, variables, r => r >= 0),
            "$lt" => EvalCompare(doc, args, variables, r => r < 0),
            "$lte" => EvalCompare(doc, args, variables, r => r <= 0),

            // Conditional
            "$cond" => EvalCond(doc, args, variables),
            "$ifNull" => EvalIfNull(doc, args, variables),
            "$switch" => EvalSwitch(doc, args, variables),

            // Boolean
            "$and" => EvalAnd(doc, args, variables),
            "$or" => EvalOr(doc, args, variables),
            "$not" => EvalNot(doc, args, variables),

            // Array
            "$arrayElemAt" => EvalArrayElemAt(doc, args, variables),
            "$size" => EvalSize(doc, args, variables),
            "$isArray" => EvalIsArray(doc, args, variables),
            "$concatArrays" => EvalConcatArrays(doc, args, variables),
            "$in" => EvalIn(doc, args, variables),
            "$filter" => EvalFilter(doc, args, variables),
            "$map" => EvalMap(doc, args, variables),
            "$reduce" => EvalReduce(doc, args, variables),
            "$reverseArray" => EvalReverseArray(doc, args, variables),
            "$range" => EvalRange(doc, args, variables),
            "$slice" => EvalSlice(doc, args, variables),
            "$first" => EvalFirst(doc, args, variables),
            "$last" => EvalLast(doc, args, variables),
            "$sortArray" => EvalSortArray(doc, args, variables),
            "$objectToArray" => EvalObjectToArray(doc, args, variables),
            "$arrayToObject" => EvalArrayToObject(doc, args, variables),
            "$zip" => EvalZip(doc, args, variables),

            // Type
            "$type" => EvalType(doc, args, variables),
            "$convert" => EvalConvert(doc, args, variables),
            "$toBool" => EvalToBool(doc, args, variables),
            "$toInt" => EvalToInt(doc, args, variables),
            "$toLong" => EvalToLong(doc, args, variables),
            "$toDouble" => EvalToDouble(doc, args, variables),
            "$toDecimal" => EvalToDecimal(doc, args, variables),
            "$toString" => EvalToString(doc, args, variables),
            "$toDate" => EvalToDate(doc, args, variables),
            "$toObjectId" => EvalToObjectId(doc, args, variables),
            "$isNumber" => EvalIsNumber(doc, args, variables),

            // Date
            "$year" => EvalDatePart(doc, args, variables, d => d.Year),
            "$month" => EvalDatePart(doc, args, variables, d => d.Month),
            "$dayOfMonth" => EvalDatePart(doc, args, variables, d => d.Day),
            "$hour" => EvalDatePart(doc, args, variables, d => d.Hour),
            "$minute" => EvalDatePart(doc, args, variables, d => d.Minute),
            "$second" => EvalDatePart(doc, args, variables, d => d.Second),
            "$millisecond" => EvalDatePart(doc, args, variables, d => d.Millisecond),
            "$dayOfWeek" => EvalDatePart(doc, args, variables, d => (int)d.DayOfWeek + 1),
            "$dayOfYear" => EvalDatePart(doc, args, variables, d => d.DayOfYear),
            "$dateToString" => EvalDateToString(doc, args, variables),
            "$dateFromString" => EvalDateFromString(doc, args, variables),
            "$dateAdd" => EvalDateAdd(doc, args, variables),
            "$dateSubtract" => EvalDateSubtract(doc, args, variables),
            "$dateDiff" => EvalDateDiff(doc, args, variables),

            // Object
            "$mergeObjects" => EvalMergeObjects(doc, args, variables),
            "$getField" => EvalGetField(doc, args, variables),
            "$setField" => EvalSetField(doc, args, variables),

            // Literal
            "$literal" => args,

            // Let
            "$let" => EvalLet(doc, args, variables),

            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/binarySize/
            //   "Returns the size in bytes of a given string or binary data value."
            "$binarySize" => EvalBinarySize(doc, args, variables),
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bsonSize/
            //   "Returns the size in bytes of a given document when encoded as BSON."
            "$bsonSize" => EvalBsonSize(doc, args, variables),
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/rand/
            //   "Returns a random float between 0 and 1."
            "$rand" => new BsonDouble(Random.Shared.NextDouble()),
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sampleRate/
            //   "Matches a random selection of input documents."
            "$sampleRate" => new BsonBoolean(Random.Shared.NextDouble() < Evaluate(doc, args, variables).ToDouble()),
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toHashedIndexKey/
            //   "Computes and returns the hash value of the input expression using the same hash function."
            "$toHashedIndexKey" => EvalToHashedIndexKey(doc, args, variables),
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/tsIncrement/
            //   "Returns the incrementing ordinal from a timestamp as a long."
            "$tsIncrement" => EvalTsIncrement(doc, args, variables),
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/tsSecond/
            //   "Returns the seconds from a timestamp as a long."
            "$tsSecond" => EvalTsSecond(doc, args, variables),

            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/function/
            //   "Defines a custom aggregation function or expression in JavaScript."
            "$function" => EvalFunction(doc, args, variables),

            _ => throw new NotSupportedException($"Expression operator '{op}' is not supported.")
        };
    }

    #region Variable Resolution

    private static BsonValue ResolveVariable(string name, BsonDocument doc, BsonDocument? variables)
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/aggregation-variables/
        //   "$$variable.field.path" — resolve variable then traverse field path

        // Handle dotted paths on variables: $$item.qty -> resolve "item", then field path "qty"
        var dotIndex = name.IndexOf('.');
        var varPart = dotIndex >= 0 ? name[..dotIndex] : name;
        var fieldPart = dotIndex >= 0 ? name[(dotIndex + 1)..] : null;

        var value = varPart switch
        {
            "ROOT" => doc,
            "CURRENT" => doc,
            "NOW" => new BsonDateTime(DateTime.UtcNow),
            "CLUSTER_TIME" => new BsonTimestamp((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1),
            "REMOVE" => RemoveSentinel, // Special sentinel — must be distinguished from null
            // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/redact/
            //   "$$KEEP, $$PRUNE, $$DESCEND are system variables used by $redact."
            "KEEP" => new BsonString("$$KEEP"),
            "PRUNE" => new BsonString("$$PRUNE"),
            "DESCEND" => new BsonString("$$DESCEND"),
            _ => variables != null && variables.Contains(varPart) ? variables[varPart] : BsonNull.Value,
        };

        // If there's a field path after the variable, resolve it
        if (fieldPart != null && value is BsonDocument valueDoc)
            return BsonFilterEvaluator.ResolveFieldPath(valueDoc, fieldPart);

        return value;
    }

    #endregion

    #region Arithmetic

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/add/
    //   "If one of the arguments is a date, $add treats the other arguments as milliseconds
    //    to add to the date."
    private static BsonValue EvalAdd(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);

        // Check if any argument is a date
        BsonDateTime? dateArg = null;
        double numericSum = 0;
        bool hasDouble = false;
        bool hasLong = false;
        foreach (var v in arr)
        {
            if (v == BsonNull.Value) return BsonNull.Value;
            if (v.BsonType == BsonType.DateTime)
            {
                if (dateArg != null)
                    throw MongoErrors.BadValue("only one date allowed in an $add expression");
                dateArg = v.AsBsonDateTime;
            }
            else
            {
                numericSum += v.ToDouble();
                if (v.BsonType == BsonType.Double || v.BsonType == BsonType.Decimal128) hasDouble = true;
                else if (v.BsonType == BsonType.Int64) hasLong = true;
            }
        }

        if (dateArg != null)
            return new BsonDateTime(dateArg.ToUniversalTime().AddMilliseconds(numericSum));

        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/add/
        //   Type promotion: integer → long → double → decimal
        return WrapNumeric(numericSum, hasDouble, hasLong);
    }

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/subtract/
    //   "If the two values are dates, return the difference in milliseconds."
    //   "If the two values are a date and a number... subtracts the number, in milliseconds, from the date."
    private static BsonValue EvalSubtract(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value || arr[1] == BsonNull.Value) return BsonNull.Value;

        bool firstIsDate = arr[0].BsonType == BsonType.DateTime;
        bool secondIsDate = arr[1].BsonType == BsonType.DateTime;

        if (firstIsDate && secondIsDate)
        {
            // Date - Date = difference in milliseconds
            var ms = (arr[0].AsBsonDateTime.ToUniversalTime() - arr[1].AsBsonDateTime.ToUniversalTime()).TotalMilliseconds;
            return new BsonInt64((long)ms);
        }

        if (firstIsDate)
        {
            // Date - number = Date minus milliseconds
            return new BsonDateTime(arr[0].AsBsonDateTime.ToUniversalTime().AddMilliseconds(-arr[1].ToDouble()));
        }

        bool hasDouble = arr[0].BsonType == BsonType.Double || arr[0].BsonType == BsonType.Decimal128
                      || arr[1].BsonType == BsonType.Double || arr[1].BsonType == BsonType.Decimal128;
        bool hasLong = arr[0].BsonType == BsonType.Int64 || arr[1].BsonType == BsonType.Int64;
        return WrapNumeric(arr[0].ToDouble() - arr[1].ToDouble(), hasDouble, hasLong);
    }

    private static BsonValue EvalMultiply(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/multiply/
        //   Type promotion: integer → long → double → decimal
        var arr = EvalArray(doc, args, variables);
        double product = 1;
        bool hasDouble = false;
        bool hasLong = false;
        foreach (var v in arr)
        {
            if (v == BsonNull.Value) return BsonNull.Value;
            product *= v.ToDouble();
            if (v.BsonType == BsonType.Double || v.BsonType == BsonType.Decimal128) hasDouble = true;
            else if (v.BsonType == BsonType.Int64) hasLong = true;
        }
        return WrapNumeric(product, hasDouble, hasLong);
    }

    private static BsonValue EvalDivide(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value || arr[1] == BsonNull.Value) return BsonNull.Value;
        var divisor = arr[1].ToDouble();
        if (divisor == 0) throw MongoErrors.BadValue("can't $divide by zero");
        return new BsonDouble(arr[0].ToDouble() / divisor);
    }

    private static BsonValue EvalMod(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/mod/
        //   Preserves type: integer % integer → integer
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value || arr[1] == BsonNull.Value) return BsonNull.Value;
        bool hasDouble = arr[0].BsonType == BsonType.Double || arr[0].BsonType == BsonType.Decimal128
                      || arr[1].BsonType == BsonType.Double || arr[1].BsonType == BsonType.Decimal128;
        bool hasLong = arr[0].BsonType == BsonType.Int64 || arr[1].BsonType == BsonType.Int64;
        return WrapNumeric(arr[0].ToDouble() % arr[1].ToDouble(), hasDouble, hasLong);
    }

    /// <summary>
    /// Wraps a numeric result in the appropriate BsonValue type based on input type promotion rules.
    /// </summary>
    /// <remarks>
    /// Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/add/
    ///   "Type promotion: integer → long → double → decimal"
    /// </remarks>
    private static BsonValue WrapNumeric(double value, bool hasDouble, bool hasLong)
    {
        if (hasDouble) return new BsonDouble(value);
        if (hasLong) return new BsonInt64((long)value);
        return new BsonInt32((int)value);
    }

    private static BsonValue EvalUnaryMath(BsonDocument doc, BsonValue args, BsonDocument? variables, Func<double, double> fn)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonDouble(fn(val.ToDouble()));
    }

    private static BsonValue EvalBinaryMath(BsonDocument doc, BsonValue args, BsonDocument? variables, Func<double, double, double> fn)
    {
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value || arr[1] == BsonNull.Value) return BsonNull.Value;
        return new BsonDouble(fn(arr[0].ToDouble(), arr[1].ToDouble()));
    }

    private static BsonValue EvalRound(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var val = arr[0].ToDouble();
        int places = arr.Count > 1 ? arr[1].ToInt32() : 0;
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/round/
        //   "Rounds using the IEEE 754 round-to-even rule."
        return new BsonDouble(Math.Round(val, places, MidpointRounding.ToEven));
    }

    private static BsonValue EvalTrunc(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var val = arr[0].ToDouble();
        int places = arr.Count > 1 ? arr[1].ToInt32() : 0;
        double factor = Math.Pow(10, places);
        return new BsonDouble(Math.Truncate(val * factor) / factor);
    }

    #endregion

    #region String

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/concat/
    private static BsonValue EvalConcat(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var sb = new StringBuilder();
        foreach (var v in arr)
        {
            if (v == BsonNull.Value) return BsonNull.Value;
            sb.Append(v.AsString);
        }
        return new BsonString(sb.ToString());
    }

    private static BsonValue EvalStringUnary(BsonDocument doc, BsonValue args, BsonDocument? variables, Func<string, string> fn)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonString(fn(val.AsString));
    }

    private static BsonValue EvalTrim(BsonDocument doc, BsonValue args, BsonDocument? variables,
        Func<string, string> fn, Func<string, char[], string> fnWithChars)
    {
        if (args.IsBsonDocument)
        {
            var spec = args.AsBsonDocument;
            var input = Evaluate(doc, spec["input"], variables);
            if (input == BsonNull.Value) return BsonNull.Value;
            var str = input.AsString;
            if (spec.Contains("chars"))
            {
                // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/ltrim/
                //   "$ltrim removes characters from the beginning of a string."
                // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/rtrim/
                //   "$rtrim removes characters from the end of a string."
                var chars = Evaluate(doc, spec["chars"], variables).AsString.ToCharArray();
                return new BsonString(fnWithChars(str, chars));
            }
            return new BsonString(fn(str));
        }
        var val = Evaluate(doc, args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonString(fn(val.AsString));
    }

    private static BsonValue EvalSubstr(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value) return new BsonString("");
        var str = arr[0].AsString;
        var start = arr[1].ToInt32();
        var length = arr[2].ToInt32();
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/substr/
        //   "If <start> is a negative number, $substr returns an empty string."
        if (start < 0) return new BsonString("");
        if (start >= str.Length) return new BsonString("");
        return new BsonString(str.Substring(start, Math.Min(length, str.Length - start)));
    }

    private static BsonValue EvalStrLen(BsonDocument doc, BsonValue args, BsonDocument? variables, Func<string, int> fn)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonInt32(fn(val.AsString));
    }

    private static BsonValue EvalIndexOf(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value) return BsonNull.Value;
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfBytes/
        //   "Returns null if the first argument is null or missing."
        if (arr[1] == BsonNull.Value) return BsonNull.Value;
        var str = arr[0].AsString;
        var sub = arr[1].AsString;
        int start = arr.Count > 2 ? arr[2].ToInt32() : 0;
        int end = arr.Count > 3 ? arr[3].ToInt32() : str.Length;
        var idx = str.IndexOf(sub, start, Math.Min(end - start, str.Length - start), StringComparison.Ordinal);
        return new BsonInt32(idx);
    }

    private static BsonValue EvalSplit(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/split/
        //   "Returns null if either argument is null."
        if (arr[0] == BsonNull.Value || arr[1] == BsonNull.Value) return BsonNull.Value;
        var parts = arr[0].AsString.Split(arr[1].AsString);
        return new BsonArray(parts.Select(p => new BsonString(p)));
    }

    private static BsonValue EvalStrcasecmp(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/strcasecmp/
        //   "Returns 1, 0, or -1."
        //   Null/missing values are treated as empty string for comparison.
        var s0 = arr[0] == BsonNull.Value ? "" : arr[0].AsString;
        var s1 = arr[1] == BsonNull.Value ? "" : arr[1].AsString;
        return new BsonInt32(Math.Sign(string.Compare(s0, s1, StringComparison.OrdinalIgnoreCase)));
    }

    private static BsonValue EvalReplaceOne(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var find = Evaluate(doc, spec["find"], variables);
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/replaceOne/
        //   "Returns null if any argument resolves to null."
        if (find == BsonNull.Value) return BsonNull.Value;
        var replacement = Evaluate(doc, spec["replacement"], variables);
        if (replacement == BsonNull.Value) return BsonNull.Value;
        var idx = input.AsString.IndexOf(find.AsString, StringComparison.Ordinal);
        if (idx < 0) return input;
        return new BsonString(input.AsString[..idx] + replacement.AsString + input.AsString[(idx + find.AsString.Length)..]);
    }

    private static BsonValue EvalReplaceAll(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var find = Evaluate(doc, spec["find"], variables);
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/replaceAll/
        //   "Returns null if any argument resolves to null."
        if (find == BsonNull.Value) return BsonNull.Value;
        var replacement = Evaluate(doc, spec["replacement"], variables);
        if (replacement == BsonNull.Value) return BsonNull.Value;
        return new BsonString(input.AsString.Replace(find.AsString, replacement.AsString));
    }

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexMatch/
    //   "regex: The regular expression. Can be specified as a string pattern or as a regex object."
    private static (string pattern, string options) ExtractRegex(BsonDocument spec, BsonDocument? variables)
    {
        var regexVal = spec["regex"];
        if (regexVal is BsonRegularExpression bre)
            return (bre.Pattern, spec.Contains("options") ? spec["options"].AsString : bre.Options);
        return (regexVal.AsString, spec.GetValue("options", "").AsString);
    }

    private static BsonValue EvalRegexMatch(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var (regex, opts) = ExtractRegex(spec, variables);
        var ro = ParseRegexOptions(opts);
        return (BsonBoolean)Regex.IsMatch(input.AsString, regex, ro);
    }

    private static BsonValue EvalRegexFind(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var (regex, opts) = ExtractRegex(spec, variables);
        var m = Regex.Match(input.AsString, regex, ParseRegexOptions(opts));
        if (!m.Success) return BsonNull.Value;
        return new BsonDocument { { "match", m.Value }, { "idx", m.Index }, { "captures", new BsonArray() } };
    }

    private static BsonValue EvalRegexFindAll(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return new BsonArray();
        var (regex, opts) = ExtractRegex(spec, variables);
        var matches = Regex.Matches(input.AsString, regex, ParseRegexOptions(opts));
        var result = new BsonArray();
        foreach (Match m in matches)
            result.Add(new BsonDocument { { "match", m.Value }, { "idx", m.Index }, { "captures", new BsonArray() } });
        return result;
    }

    private static RegexOptions ParseRegexOptions(string opts)
    {
        var ro = RegexOptions.None;
        if (opts.Contains('i')) ro |= RegexOptions.IgnoreCase;
        if (opts.Contains('m')) ro |= RegexOptions.Multiline;
        if (opts.Contains('s')) ro |= RegexOptions.Singleline;
        if (opts.Contains('x')) ro |= RegexOptions.IgnorePatternWhitespace;
        return ro;
    }

    #endregion

    #region Comparison

    private static BsonValue EvalCmp(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        return new BsonInt32(BsonValueComparer.Instance.Compare(arr[0], arr[1]));
    }

    private static BsonValue EvalCompare(BsonDocument doc, BsonValue args, BsonDocument? variables, Func<int, bool> pred)
    {
        var arr = EvalArray(doc, args, variables);
        var cmp = BsonValueComparer.Instance.Compare(arr[0], arr[1]);
        return (BsonBoolean)pred(cmp);
    }

    #endregion

    #region Conditional

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/cond/
    //   "$cond requires exactly 3 arguments in array form: [if, then, else]."
    private static BsonValue EvalCond(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        if (args is BsonArray arr3)
        {
            if (arr3.Count != 3)
                throw MongoErrors.BadValue("$cond requires exactly 3 arguments in array form: [if, then, else]");
            var condition = Evaluate(doc, arr3[0], variables);
            return IsTruthy(condition) ? Evaluate(doc, arr3[1], variables) : Evaluate(doc, arr3[2], variables);
        }
        var spec = args.AsBsonDocument;
        var cond = Evaluate(doc, spec["if"], variables);
        return IsTruthy(cond) ? Evaluate(doc, spec["then"], variables) : Evaluate(doc, spec["else"], variables);
    }

    private static BsonValue EvalIfNull(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        foreach (var v in arr)
        {
            if (v != BsonNull.Value) return v;
        }
        return arr[^1];
    }

    private static BsonValue EvalSwitch(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var branches = spec["branches"].AsBsonArray;
        foreach (var branch in branches)
        {
            var b = branch.AsBsonDocument;
            var caseVal = Evaluate(doc, b["case"], variables);
            if (IsTruthy(caseVal))
                return Evaluate(doc, b["then"], variables);
        }
        if (spec.Contains("default"))
            return Evaluate(doc, spec["default"], variables);
        throw MongoErrors.BadValue("$switch: no matching branch and no default");
    }

    #endregion

    #region Boolean

    private static BsonValue EvalAnd(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        return (BsonBoolean)arr.All(IsTruthy);
    }

    private static BsonValue EvalOr(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        return (BsonBoolean)arr.Any(IsTruthy);
    }

    private static BsonValue EvalNot(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        return (BsonBoolean)!IsTruthy(arr[0]);
    }

    #endregion

    #region Array

    private static BsonValue EvalArrayElemAt(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        if (arr[0] == BsonNull.Value) return BsonNull.Value;
        var array = arr[0].AsBsonArray;
        var idx = arr[1].ToInt32();
        if (idx < 0) idx += array.Count;
        if (idx < 0 || idx >= array.Count) return BsonNull.Value;
        return array[idx];
    }

    private static BsonValue EvalSize(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonInt32(val.AsBsonArray.Count);
    }

    private static BsonValue EvalIsArray(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        return (BsonBoolean)val.IsBsonArray;
    }

    private static BsonValue EvalConcatArrays(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var result = new BsonArray();
        foreach (var v in arr)
        {
            if (v == BsonNull.Value) return BsonNull.Value;
            result.AddRange(v.AsBsonArray);
        }
        return result;
    }

    private static BsonValue EvalIn(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var value = arr[0];
        var array = arr[1].AsBsonArray;
        return (BsonBoolean)array.Any(x => x.Equals(value));
    }

    private static BsonValue EvalFilter(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var asVar = spec.GetValue("as", "this").AsString;
        var cond = spec["cond"];
        var limit = spec.Contains("limit") ? Evaluate(doc, spec["limit"], variables).ToInt32() : int.MaxValue;

        var result = new BsonArray();
        foreach (var item in input.AsBsonArray)
        {
            if (result.Count >= limit) break;
            var vars = new BsonDocument(variables ?? new BsonDocument()) { { asVar, item } };
            var condResult = Evaluate(doc, cond, vars);
            if (IsTruthy(condResult))
                result.Add(item);
        }
        return result;
    }

    private static BsonValue EvalMap(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var asVar = spec.GetValue("as", "this").AsString;
        var inExpr = spec["in"];

        var result = new BsonArray();
        foreach (var item in input.AsBsonArray)
        {
            var vars = new BsonDocument(variables ?? new BsonDocument()) { { asVar, item } };
            result.Add(Evaluate(doc, inExpr, vars));
        }
        return result;
    }

    private static BsonValue EvalReduce(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var initialValue = Evaluate(doc, spec["initialValue"], variables);
        var inExpr = spec["in"];

        var value = initialValue;
        foreach (var item in input.AsBsonArray)
        {
            var vars = new BsonDocument(variables ?? new BsonDocument()) { { "value", value }, { "this", item } };
            value = Evaluate(doc, inExpr, vars);
        }
        return value;
    }

    private static BsonValue EvalReverseArray(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonArray(val.AsBsonArray.Reverse());
    }

    private static BsonValue EvalRange(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        int start = arr[0].ToInt32();
        int end = arr[1].ToInt32();
        int step = arr.Count > 2 ? arr[2].ToInt32() : 1;
        var result = new BsonArray();
        for (int i = start; step > 0 ? i < end : i > end; i += step)
            result.Add(new BsonInt32(i));
        return result;
    }

    private static BsonValue EvalSlice(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var array = arr[0].AsBsonArray;
        if (arr.Count == 2)
        {
            int n = arr[1].ToInt32();
            if (n >= 0) return new BsonArray(array.Take(n));
            return new BsonArray(array.Skip(Math.Max(0, array.Count + n)));
        }
        int pos = arr[1].ToInt32();
        int count = arr[2].ToInt32();
        if (pos < 0) pos = Math.Max(0, array.Count + pos);
        return new BsonArray(array.Skip(pos).Take(count));
    }

    private static BsonValue EvalFirst(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value || val is not BsonArray arr || arr.Count == 0) return BsonNull.Value;
        return arr[0];
    }

    private static BsonValue EvalLast(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value || val is not BsonArray arr || arr.Count == 0) return BsonNull.Value;
        return arr[^1];
    }

    private static BsonValue EvalSortArray(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        if (input == BsonNull.Value) return BsonNull.Value;
        var sortBy = spec["sortBy"];
        if (sortBy.IsBsonDocument)
        {
            var sorted = BsonSortEvaluator.Apply(input.AsBsonArray.Select(x => x.AsBsonDocument).ToList(), sortBy.AsBsonDocument);
            return new BsonArray(sorted);
        }
        // Simple numeric sort direction
        var dir = sortBy.ToInt32();
        var items = input.AsBsonArray.ToList();
        items.Sort((a, b) => dir * BsonValueComparer.Instance.Compare(a, b));
        return new BsonArray(items);
    }

    private static BsonValue EvalObjectToArray(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        var result = new BsonArray();
        foreach (var el in val.AsBsonDocument)
            result.Add(new BsonDocument { { "k", el.Name }, { "v", el.Value } });
        return result;
    }

    private static BsonValue EvalArrayToObject(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        var result = new BsonDocument();
        foreach (var item in val.AsBsonArray)
        {
            if (item.IsBsonDocument)
            {
                var d = item.AsBsonDocument;
                result[d["k"].AsString] = d["v"];
            }
            else if (item.IsBsonArray)
            {
                var pair = item.AsBsonArray;
                result[pair[0].AsString] = pair[1];
            }
        }
        return result;
    }

    private static BsonValue EvalZip(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var inputs = spec["inputs"].AsBsonArray.Select(i => Evaluate(doc, i, variables).AsBsonArray).ToList();
        var useLongestLength = spec.GetValue("useLongestLength", false).AsBoolean;
        var defaults = spec.Contains("defaults") ? spec["defaults"].AsBsonArray : null;

        int len = useLongestLength ? inputs.Max(a => a.Count) : inputs.Min(a => a.Count);
        var result = new BsonArray();
        for (int i = 0; i < len; i++)
        {
            var row = new BsonArray();
            for (int j = 0; j < inputs.Count; j++)
            {
                if (i < inputs[j].Count)
                    row.Add(inputs[j][i]);
                else
                    row.Add(defaults != null && j < defaults.Count ? defaults[j] : BsonNull.Value);
            }
            result.Add(row);
        }
        return result;
    }

    #endregion

    #region Type

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/type/
    private static BsonValue EvalType(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        return new BsonString(val.BsonType switch
        {
            BsonType.Double => "double",
            BsonType.String => "string",
            BsonType.Document => "object",
            BsonType.Array => "array",
            BsonType.Binary => "binData",
            BsonType.ObjectId => "objectId",
            BsonType.Boolean => "bool",
            BsonType.DateTime => "date",
            BsonType.Null => "null",
            BsonType.RegularExpression => "regex",
            BsonType.Int32 => "int",
            BsonType.Timestamp => "timestamp",
            BsonType.Int64 => "long",
            BsonType.Decimal128 => "decimal",
            _ => "missing"
        });
    }

    private static BsonValue EvalConvert(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var input = Evaluate(doc, spec["input"], variables);
        var toVal = Evaluate(doc, spec["to"], variables);
        var onError = spec.Contains("onError") ? spec["onError"] : null;
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
        //   "onNull: The value to return if the input is null or missing."
        var onNull = spec.Contains("onNull") ? spec["onNull"] : null;

        if (input == BsonNull.Value)
            return onNull != null ? Evaluate(doc, onNull, variables) : BsonNull.Value;

        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
        //   "to: Can be any valid expression that resolves to one of the following numeric
        //    or string identifiers."
        var to = toVal.IsNumeric ? BsonTypeCodeToName(toVal.ToInt32()) : toVal.AsString;
        try
        {
            return ConvertTo(input, to);
        }
        catch
        {
            if (onError != null) return Evaluate(doc, onError, variables);
            throw;
        }
    }

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
    //   "to: ... numeric BSON type identifiers."
    private static string BsonTypeCodeToName(int code) => code switch
    {
        1 => "double",
        2 => "string",
        7 => "objectId",
        8 => "bool",
        9 => "date",
        16 => "int",
        18 => "long",
        19 => "decimal",
        _ => throw MongoErrors.BadValue($"Unknown BSON type code: {code}")
    };

    private static BsonValue ConvertTo(BsonValue input, string type)
    {
        if (input == BsonNull.Value) return BsonNull.Value;
        return type switch
        {
            "double" => new BsonDouble(input.ToDouble()),
            "string" => new BsonString(input.ToString()!),
            "bool" => (BsonBoolean)IsTruthy(input),
            "int" => new BsonInt32(input.ToInt32()),
            "long" => new BsonInt64(input.ToInt64()),
            "decimal" => new BsonDecimal128(input.ToDecimal()),
            "date" => input.IsString ? new BsonDateTime(DateTime.Parse(input.AsString, CultureInfo.InvariantCulture)) : new BsonDateTime(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(input.ToInt64())),
            "objectId" => input.IsString ? new BsonObjectId(ObjectId.Parse(input.AsString)) : input,
            _ => input
        };
    }

    private static BsonValue EvalToBool(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        return (BsonBoolean)IsTruthy(val);
    }

    private static BsonValue EvalToInt(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonInt32(val.ToInt32());
    }

    private static BsonValue EvalToLong(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonInt64(val.ToInt64());
    }

    private static BsonValue EvalToDouble(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonDouble(val.ToDouble());
    }

    private static BsonValue EvalToDecimal(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonDecimal128(val.ToDecimal());
    }

    private static BsonValue EvalToString(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonString(val.ToString()!);
    }

    private static BsonValue EvalToDate(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        if (val.IsString) return new BsonDateTime(DateTime.Parse(val.AsString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        return new BsonDateTime(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(val.ToInt64()));
    }

    private static BsonValue EvalToObjectId(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        return new BsonObjectId(ObjectId.Parse(val.AsString));
    }

    private static BsonValue EvalIsNumber(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        return (BsonBoolean)(val.BsonType is BsonType.Int32 or BsonType.Int64 or BsonType.Double or BsonType.Decimal128);
    }

    #endregion

    #region Date

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation-date/
    private static BsonValue EvalDatePart(BsonDocument doc, BsonValue args, BsonDocument? variables, Func<DateTime, int> fn)
    {
        var val = Evaluate(doc, args is BsonArray a ? a[0] : args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        var dt = val.ToUniversalTime();
        return new BsonInt32(fn(dt));
    }

    private static BsonValue EvalDateToString(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var date = Evaluate(doc, spec["date"], variables);
        if (date == BsonNull.Value) return spec.Contains("onNull") ? Evaluate(doc, spec["onNull"], variables) : BsonNull.Value;
        var format = spec.GetValue("format", "%Y-%m-%dT%H:%M:%S.%LZ").AsString;
        var dt = date.ToUniversalTime();
        // Convert MongoDB format to .NET format
        var netFormat = format
            .Replace("%Y", "yyyy").Replace("%m", "MM").Replace("%d", "dd")
            .Replace("%H", "HH").Replace("%M", "mm").Replace("%S", "ss")
            .Replace("%L", "fff").Replace("%Z", "zzz");
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateToString/
        //   "%j: Day of year (3 digits, zero padded). Valid values: 001-366."
        //   .NET has no built-in single format specifier for day-of-year, so we handle it manually.
        if (netFormat.Contains("%j"))
            netFormat = netFormat.Replace("%j", dt.DayOfYear.ToString("D3"));
        return new BsonString(dt.ToString(netFormat, CultureInfo.InvariantCulture));
    }

    private static BsonValue EvalDateFromString(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var dateString = Evaluate(doc, spec["dateString"], variables);
        if (dateString == BsonNull.Value) return spec.Contains("onNull") ? Evaluate(doc, spec["onNull"], variables) : BsonNull.Value;
        try
        {
            var dt = DateTime.Parse(dateString.AsString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return new BsonDateTime(dt);
        }
        catch
        {
            if (spec.Contains("onError")) return Evaluate(doc, spec["onError"], variables);
            throw;
        }
    }

    private static BsonValue EvalDateAdd(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var startDate = Evaluate(doc, spec["startDate"], variables).ToUniversalTime();
        var unit = spec["unit"].AsString;
        var amount = Evaluate(doc, spec["amount"], variables).ToInt64();
        return new BsonDateTime(AddToDate(startDate, unit, amount));
    }

    private static BsonValue EvalDateSubtract(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var startDate = Evaluate(doc, spec["startDate"], variables).ToUniversalTime();
        var unit = spec["unit"].AsString;
        var amount = Evaluate(doc, spec["amount"], variables).ToInt64();
        return new BsonDateTime(AddToDate(startDate, unit, -amount));
    }

    private static BsonValue EvalDateDiff(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var start = Evaluate(doc, spec["startDate"], variables).ToUniversalTime();
        var end = Evaluate(doc, spec["endDate"], variables).ToUniversalTime();
        var unit = spec["unit"].AsString;
        var diff = end - start;
        long result = unit switch
        {
            "millisecond" => (long)diff.TotalMilliseconds,
            "second" => (long)diff.TotalSeconds,
            "minute" => (long)diff.TotalMinutes,
            "hour" => (long)diff.TotalHours,
            "day" => (long)diff.TotalDays,
            "week" => (long)diff.TotalDays / 7,
            "month" => (end.Year - start.Year) * 12 + (end.Month - start.Month),
            "year" => end.Year - start.Year,
            _ => throw MongoErrors.BadValue($"Unknown date unit: {unit}")
        };
        return new BsonInt64(result);
    }

    private static DateTime AddToDate(DateTime dt, string unit, long amount)
    {
        return unit switch
        {
            "millisecond" => dt.AddMilliseconds(amount),
            "second" => dt.AddSeconds(amount),
            "minute" => dt.AddMinutes(amount),
            "hour" => dt.AddHours(amount),
            "day" => dt.AddDays(amount),
            "week" => dt.AddDays(amount * 7),
            "month" => dt.AddMonths((int)amount),
            "year" => dt.AddYears((int)amount),
            _ => throw MongoErrors.BadValue($"Unknown date unit: {unit}")
        };
    }

    #endregion

    #region Object

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/mergeObjects/
    private static BsonValue EvalMergeObjects(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var arr = EvalArray(doc, args, variables);
        var result = new BsonDocument();
        foreach (var v in arr)
        {
            if (v == BsonNull.Value) continue;
            foreach (var el in v.AsBsonDocument)
                result[el.Name] = el.Value;
        }
        return result;
    }

    private static BsonValue EvalGetField(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        if (args.IsString)
            return BsonFilterEvaluator.ResolveFieldPath(doc, args.AsString);
        var spec = args.AsBsonDocument;
        var field = Evaluate(doc, spec["field"], variables).AsString;
        var input = spec.Contains("input") ? Evaluate(doc, spec["input"], variables).AsBsonDocument : doc;
        return input.Contains(field) ? input[field] : BsonNull.Value;
    }

    private static BsonValue EvalSetField(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var field = Evaluate(doc, spec["field"], variables).AsString;
        var input = Evaluate(doc, spec["input"], variables).AsBsonDocument.DeepClone().AsBsonDocument;
        var value = Evaluate(doc, spec["value"], variables);
        input[field] = value;
        return input;
    }

    #endregion

    #region Let

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/let/
    private static BsonValue EvalLet(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var vars = spec["vars"].AsBsonDocument;
        var inExpr = spec["in"];
        var newVars = new BsonDocument(variables ?? new BsonDocument());
        foreach (var v in vars)
            newVars[v.Name] = Evaluate(doc, v.Value, variables);
        return Evaluate(doc, inExpr, newVars);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Evaluate an array of expressions, or if args is already evaluated, wrap single value.
    /// </summary>
    private static List<BsonValue> EvalArray(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        if (args is BsonArray arr)
            return arr.Select(a => Evaluate(doc, a, variables)).ToList();
        return [Evaluate(doc, args, variables)];
    }

    /// <summary>
    /// MongoDB truthiness: false, null, 0, undefined → false; everything else → true.
    /// </summary>
    internal static bool IsTruthy(BsonValue value)
    {
        if (value == BsonNull.Value) return false;
        if (value.IsBoolean) return value.AsBoolean;
        if (value.IsNumeric) return value.ToDouble() != 0;
        return true;
    }

    #endregion

    #region Size Operators

    private static BsonValue EvalBinarySize(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        if (val.BsonType == BsonType.String)
            return new BsonInt32(System.Text.Encoding.UTF8.GetByteCount(val.AsString));
        if (val.BsonType == BsonType.Binary)
            return new BsonInt32(val.AsByteArray.Length);
        return BsonNull.Value;
    }

    private static BsonValue EvalBsonSize(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args, variables);
        if (val == BsonNull.Value) return BsonNull.Value;
        if (val is BsonDocument bsonDoc)
            return new BsonInt32(bsonDoc.ToBson().Length);
        return BsonNull.Value;
    }

    #endregion

    #region Hash / Timestamp Operators

    private static BsonValue EvalToHashedIndexKey(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args, variables);
        // Use a simple hash — MongoDB uses md5-based hashing internally
        var hash = val.GetHashCode();
        return new BsonInt64(hash);
    }

    private static BsonValue EvalTsIncrement(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args, variables);
        if (val.BsonType != BsonType.Timestamp) return BsonNull.Value;
        return new BsonInt64(val.AsBsonTimestamp.Increment);
    }

    private static BsonValue EvalTsSecond(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var val = Evaluate(doc, args, variables);
        if (val.BsonType != BsonType.Timestamp) return BsonNull.Value;
        return new BsonInt64(val.AsBsonTimestamp.Timestamp);
    }

    #endregion

    #region $function

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/function/
    //   "Defines a custom aggregation function or expression in JavaScript."
    //   "The body of the function is a string containing a JavaScript function."
    // Note: Actual JavaScript execution requires the JsTriggers package.
    private static Func<string, BsonArray, BsonValue>? _functionEvaluator;

    /// <summary>
    /// Registers a custom $function evaluator (typically from the JsTriggers package).
    /// </summary>
    internal static void RegisterFunctionEvaluator(Func<string, BsonArray, BsonValue> evaluator)
    {
        _functionEvaluator = evaluator;
    }

    private static BsonValue EvalFunction(BsonDocument doc, BsonValue args, BsonDocument? variables)
    {
        var spec = args.AsBsonDocument;
        var body = spec["body"].AsString;
        var fnArgs = spec.Contains("args") ? spec["args"].AsBsonArray : new BsonArray();

        // Evaluate argument expressions against the current document
        var evaluatedArgs = new BsonArray();
        foreach (var arg in fnArgs)
        {
            evaluatedArgs.Add(Evaluate(doc, arg, variables));
        }

        if (_functionEvaluator != null)
            return _functionEvaluator(body, evaluatedArgs);

        throw new NotSupportedException(
            "$function requires the MongoDB.InMemoryEmulator.JsTriggers package for JavaScript execution. " +
            "Install the package and call JsExpressionSetup.Register() to enable $function support.");
    }

    #endregion

    #region $accumulator (hook)

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/accumulator/
    //   "Defines a custom accumulator function in JavaScript."
    private static Func<BsonDocument, List<BsonDocument>, BsonDocument?, BsonValue>? _accumulatorEvaluator;

    /// <summary>
    /// Registers a custom $accumulator evaluator (typically from the JsTriggers package).
    /// Takes (accSpec, groupDocs, variables) and returns the accumulated result.
    /// </summary>
    internal static void RegisterAccumulatorEvaluator(Func<BsonDocument, List<BsonDocument>, BsonDocument?, BsonValue> evaluator)
    {
        _accumulatorEvaluator = evaluator;
    }

    internal static BsonValue EvalAccumulator(BsonDocument accSpec, List<BsonDocument> groupDocs, BsonDocument? variables)
    {
        if (_accumulatorEvaluator != null)
            return _accumulatorEvaluator(accSpec, groupDocs, variables);

        throw new NotSupportedException(
            "$accumulator requires the MongoDB.InMemoryEmulator.JsTriggers package for JavaScript execution. " +
            "Install the package and call JsExpressionSetup.Register() to enable $accumulator support.");
    }

    #endregion
}
