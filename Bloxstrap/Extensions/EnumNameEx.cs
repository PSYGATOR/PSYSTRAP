using System.Reflection;

namespace Bloxstrap.Extensions
{
    public static class EnumNameEx
    {
        /// <summary>Display name from [EnumName(StaticName = ...)], falling back to the member name.</summary>
        public static string ToEnumName(this Enum value)
        {
            var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
            return member?.GetCustomAttribute<EnumNameAttribute>()?.StaticName ?? value.ToString();
        }
    }
}
