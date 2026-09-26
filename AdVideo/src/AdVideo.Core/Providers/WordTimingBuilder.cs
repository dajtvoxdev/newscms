using System.Text;

namespace AdVideo.Core.Providers;

/// <summary>
/// Ghép mốc theo ký tự thành mốc theo từ.
/// </summary>
/// <remarks>
/// <para>
/// Tách khỏi adapter ElevenLabs để adapter viết tay và engine descriptor dùng CHUNG một luật: hai
/// cách ghép khác nhau là hai timeline khác nhau cho cùng một câu thoại, và không so sánh được.
/// </para>
/// <para>
/// Gộp bằng khoảng trắng. Dấu câu dính vào từ liền trước là có chủ đích: phụ đề hiển thị
/// "Xin chào," chứ không tách dấu phẩy thành một "từ" riêng dài ba mươi mili giây.
/// </para>
/// </remarks>
public static class WordTimingBuilder
{
    public static IReadOnlyList<WordTiming> FromCharacters(IReadOnlyList<CharacterTiming> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);

        var words = new List<WordTiming>();
        var buffer = new StringBuilder();

        double start = 0;
        double end = 0;

        foreach (CharacterTiming character in characters)
        {
            if (char.IsWhiteSpace(character.Character))
            {
                Flush();

                continue;
            }

            if (buffer.Length == 0)
            {
                start = character.StartSeconds;
            }

            buffer.Append(character.Character);
            end = character.EndSeconds;
        }

        Flush();

        return words;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            words.Add(new WordTiming(buffer.ToString(), start, end));
            buffer.Clear();
        }
    }
}
