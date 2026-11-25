using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MopedSim.Data
{
    /// <summary>
    /// Strict, path-aware JSON accessors. Every failure is reported as a <see cref="CatalogLoadException"/>
    /// naming the catalog file and the JSON location, never as a silently-applied default.
    /// </summary>
    internal static class JsonRead
    {
        public static JObject ParseRootObject(string json, string source)
        {
            if (json == null)
            {
                throw new CatalogLoadException(source, "JSON text is null.");
            }

            try
            {
                using (var textReader = new StringReader(json))
                using (var reader = new JsonTextReader(textReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    JToken root = JToken.ReadFrom(reader);
                    if (reader.Read() && reader.TokenType != JsonToken.Comment)
                    {
                        throw new CatalogLoadException(source, "unexpected trailing content after root JSON value.");
                    }
                    if (!(root is JObject obj))
                    {
                        throw new CatalogLoadException(source, "root JSON value must be an object, got " + root.Type + ".");
                    }
                    return obj;
                }
            }
            catch (JsonException ex)
            {
                throw new CatalogLoadException(source, "invalid JSON (" + ex.Message + ")", ex);
            }
        }

        public static string Ctx(string parent, string key)
        {
            return parent + "." + key;
        }

        public static JArray RequireArray(JObject obj, string key, string ctx)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw Fail(ctx, "missing required array '" + key + "'.");
            }
            if (!(token is JArray arr))
            {
                throw Fail(ctx, "'" + key + "' must be an array, got " + token.Type + ".");
            }
            return arr;
        }

        public static JObject RequireObject(JObject obj, string key, string ctx)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw Fail(ctx, "missing required object '" + key + "'.");
            }
            if (!(token is JObject child))
            {
                throw Fail(ctx, "'" + key + "' must be an object, got " + token.Type + ".");
            }
            return child;
        }

        public static JObject? OptionalObject(JObject obj, string key, string ctx)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (!(token is JObject child))
            {
                throw Fail(ctx, "'" + key + "' must be an object, got " + token.Type + ".");
            }
            return child;
        }

        public static JObject AsObject(JToken token, string ctx)
        {
            if (!(token is JObject obj))
            {
                throw Fail(ctx, "expected an object, got " + token.Type + ".");
            }
            return obj;
        }

        public static string RequireString(JObject obj, string key, string ctx)
        {
            string? value = OptionalString(obj, key, ctx);
            if (value == null || value.Length == 0)
            {
                throw Fail(ctx, "missing required non-empty string '" + key + "'.");
            }
            return value;
        }

        public static string? OptionalString(JObject obj, string key, string ctx)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.String)
            {
                throw Fail(ctx, "'" + key + "' must be a string, got " + token.Type + ".");
            }
            return (string?)token;
        }

        public static double RequireNumber(JObject obj, string key, string ctx)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw Fail(ctx, "missing required number '" + key + "'.");
            }
            return AsNumber(token, Ctx(ctx, key));
        }

        public static double OptionalNumber(JObject obj, string key, string ctx, double fallback)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }
            return AsNumber(token, Ctx(ctx, key));
        }

        public static double AsNumber(JToken token, string ctx)
        {
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                double value = token.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw Fail(ctx, "number must be finite.");
                }
                return value;
            }
            throw Fail(ctx, "must be a number, got " + token.Type + ".");
        }

        public static int RequireInt(JObject obj, string key, string ctx)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw Fail(ctx, "missing required integer '" + key + "'.");
            }
            return AsInt(token, Ctx(ctx, key));
        }

        public static int OptionalInt(JObject obj, string key, string ctx, int fallback)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }
            return AsInt(token, Ctx(ctx, key));
        }

        public static int AsInt(JToken token, string ctx)
        {
            if (token.Type != JTokenType.Integer)
            {
                throw Fail(ctx, "must be an integer, got " + token.Type + ".");
            }
            long value = token.Value<long>();
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw Fail(ctx, "integer out of range.");
            }
            return (int)value;
        }

        public static bool OptionalBool(JObject obj, string key, string ctx, bool fallback)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }
            if (token.Type != JTokenType.Boolean)
            {
                throw Fail(ctx, "'" + key + "' must be a boolean, got " + token.Type + ".");
            }
            return token.Value<bool>();
        }

        public static List<string> OptionalStringList(JObject obj, string key, string ctx)
        {
            var result = new List<string>();
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return result;
            }
            if (!(token is JArray arr))
            {
                throw Fail(ctx, "'" + key + "' must be an array of strings, got " + token.Type + ".");
            }
            for (int i = 0; i < arr.Count; i++)
            {
                JToken item = arr[i];
                if (item.Type != JTokenType.String)
                {
                    throw Fail(ctx, "'" + key + "[" + i + "]' must be a string, got " + item.Type + ".");
                }
                string? s = (string?)item;
                if (s == null || s.Length == 0)
                {
                    throw Fail(ctx, "'" + key + "[" + i + "]' must be a non-empty string.");
                }
                result.Add(s);
            }
            return result;
        }

        public static CatalogVector3 OptionalVector3(JObject obj, string key, string ctx)
        {
            JObject? v = OptionalObject(obj, key, ctx);
            if (v == null)
            {
                return new CatalogVector3(0f, 0f, 0f);
            }
            string vCtx = Ctx(ctx, key);
            return new CatalogVector3(
                (float)RequireNumber(v, "x", vCtx),
                (float)RequireNumber(v, "y", vCtx),
                (float)RequireNumber(v, "z", vCtx));
        }

        public static CatalogLoadException Fail(string ctx, string message)
        {
            int colon = ctx.IndexOf(':');
            string source = colon > 0 ? ctx.Substring(0, colon) : ctx;
            string where = colon > 0 ? ctx.Substring(colon + 1).TrimStart() : string.Empty;
            return new CatalogLoadException(source, (where.Length > 0 ? where + ": " : string.Empty) + message);
        }
    }
}
