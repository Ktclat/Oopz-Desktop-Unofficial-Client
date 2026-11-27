using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Oopz.Native
{
    internal sealed class ChatArea
    {
        public string Name { get; set; }
        public List<ChatChannel> Channels { get; set; }
        public override string ToString() { return Name; }
    }

    internal sealed class ChatChannel
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public bool IsVoice { get; set; }
        public string Draft { get; set; }
        public ObservableCollection<ChatLine> Lines { get; private set; }
        readonly Dictionary<string, ChatLine> seen = new Dictionary<string, ChatLine>();
        public ChatChannel() { Lines = new ObservableCollection<ChatLine>(); }
        public override string ToString() { return Label; }

        public void Append(ChatLine line, int limit)
        {
            if (!String.IsNullOrEmpty(line.Id) && seen.ContainsKey(line.Id)) return;
            // Cached events and live events can arrive together while connecting.
            int position = Lines.Count;
            while (position > 0 && line.Time.HasValue && Lines[position - 1].Time > line.Time) position--;
            Lines.Insert(position, line);
            if (!String.IsNullOrEmpty(line.Id)) seen.Add(line.Id, line);
            if (Lines.Count > limit)
            {
                var first = Lines[0];
                if (!String.IsNullOrEmpty(first.Id)) seen.Remove(first.Id);
                Lines.RemoveAt(0);
            }
        }
    }

    internal sealed class ChatLine
    {
        public string Id { get; set; }
        public string Author { get; set; }
        public string Text { get; set; }
        public DateTimeOffset? Time { get; set; }
        public string Clock { get { return Time.HasValue ? Time.Value.ToLocalTime().ToString("MM-dd HH:mm") : ""; } }
        public override string ToString() { return Author + " " + Text; }
    }
}
