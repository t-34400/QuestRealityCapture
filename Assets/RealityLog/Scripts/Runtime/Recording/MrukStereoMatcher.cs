#nullable enable

using System;
using System.Collections.Generic;

namespace RealityLog.Recording
{
    internal static class MrukStereoMatcher
    {
        internal readonly struct Frame
        {
            public Frame(int index, string fileName, long timestampUs, bool isLeft)
            {
                Index = index;
                FileName = fileName;
                TimestampUs = timestampUs;
                IsLeft = isLeft;
            }

            public int Index { get; }
            public string FileName { get; }
            public long TimestampUs { get; }
            public bool IsLeft { get; }
        }

        public static void Match(List<Frame> frames, Action<Frame, Frame> writePair)
        {
            frames.Sort((a, b) =>
                (a.TimestampUs, a.IsLeft, a.Index).CompareTo((b.TimestampUs, b.IsLeft, b.Index)));

            // Group equal timestamps from one camera so ties use the earliest remaining frame.
            var groups = new LinkedList<Group>();
            for (var start = 0; start < frames.Count;)
            {
                var end = start + 1;
                while (end < frames.Count && frames[end].TimestampUs == frames[start].TimestampUs
                    && frames[end].IsLeft == frames[start].IsLeft)
                {
                    end++;
                }
                groups.AddLast(new Group(start, end));
                start = end;
            }

            // The closest opposite-camera groups are adjacent in timestamp order. Removing a
            // pair only changes candidates touching those groups and their immediate neighbors.
            var candidates = new SortedSet<Candidate>();
            for (var group = groups.First; group != null; group = group.Next)
            {
                AddCandidate(group);
            }

            while (candidates.Count > 0)
            {
                var candidate = candidates.Min;
                candidates.Remove(candidate);
                if (!candidate.IsCurrent)
                {
                    continue;
                }

                writePair(candidate.Left, candidate.Right);
                var before = candidate.First.Previous;
                var after = candidate.Second.Next;
                Consume(candidate.First);
                Consume(candidate.Second);

                for (var group = before ?? groups.First; group != after && group != null; group = group.Next)
                {
                    AddCandidate(group);
                }
            }

            void AddCandidate(LinkedListNode<Group> first)
            {
                var second = first.Next;
                if (second == null)
                {
                    return;
                }

                var a = frames[first.Value.Start];
                var b = frames[second.Value.Start];
                if (a.IsLeft != b.IsLeft)
                {
                    candidates.Add(new Candidate(first, second, a, b));
                }
            }

            void Consume(LinkedListNode<Group> group)
            {
                var range = group.Value;
                range.Start++;
                if (range.Start == range.End)
                {
                    groups.Remove(group);
                }
                else
                {
                    group.Value = range;
                }
            }
        }

        private struct Group
        {
            public Group(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start;
            public readonly int End;
        }

        private readonly struct Candidate : IComparable<Candidate>
        {
            private readonly int firstStart;
            private readonly int secondStart;

            public Candidate(LinkedListNode<Group> first, LinkedListNode<Group> second, Frame a, Frame b)
            {
                First = first;
                Second = second;
                firstStart = first.Value.Start;
                secondStart = second.Value.Start;
                Left = a.IsLeft ? a : b;
                Right = a.IsLeft ? b : a;
            }

            public LinkedListNode<Group> First { get; }
            public LinkedListNode<Group> Second { get; }
            public Frame Left { get; }
            public Frame Right { get; }

            public bool IsCurrent => First.Next == Second
                && First.Value.Start == firstStart && Second.Value.Start == secondStart;

            public int CompareTo(Candidate other)
            {
                return (Distance, Left.Index, Right.Index)
                    .CompareTo((other.Distance, other.Left.Index, other.Right.Index));
            }

            private long Distance => Math.Abs(Right.TimestampUs - Left.TimestampUs);
        }
    }
}
