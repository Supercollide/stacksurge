using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>What the Game Over screen shows for one finished run.</summary>
    public struct GameOverData
    {
        public int Score;
        public bool IsNewBest;
        public float SecondsSurvived;
        public int DailyBest;
        public int AllTimeHigh;
        public int Streak;
        /// <summary>This run landed on a 7th day in a row and added the streak reward to the next run.</summary>
        public bool StreakRewardThisRun;
        /// <summary>Challenge progress when the run started, so the bars can show what this run added. May be null.</summary>
        public ChallengeDisplayData[] ChallengesBefore;
        public ChallengeDisplayData[] Challenges;
        /// <summary>All of today's challenges are done and their bonus was added (this run, or an earlier one today).</summary>
        public bool AllDoneBonusGranted;
        public bool AllDoneBonusThisRun;
    }

    /// <summary>
    /// The Game Over screen, built in code inside the scene's GameOver root. Top to bottom: the result (label, score,
    /// then time / today / best in an inset strip), today's challenges with what this run added and a claim button for
    /// finished ones, the streak with this week's played days, Retry, Main Menu and a link to the leaderboard.
    /// The scene's Retry and Main Menu buttons are moved into it, so their click wiring stays in GameView.
    /// </summary>
    public sealed class GameOverPanel
    {
        const float PanelWidth = 900f;
        const float ScreenMargin = 40f;
        const float RowHeight = 124f;
        const float RowValueWidth = 240f;
        const float ButtonRadius = 24f;
        const float RetryHeight = 112f;

        static readonly Color GameOverLabel = new Color32(0xF0, 0x7A, 0x7E, 0xFF);   // soft red, 6:1 on the panel
        static readonly Color DarkLabel = new Color32(0x12, 0x1A, 0x2B, 0xFF);       // text on the yellow claim button
        static readonly string[] DayNumbers = { "1", "2", "3", "4", "5", "6", "7" };

        readonly RectTransform _root;
        readonly RectTransform _panel;
        readonly TMP_FontAsset _font;
        readonly Action<int> _onClaim;
        readonly Func<TimeSpan> _timeUntilReset;

        readonly TextMeshProUGUI _header, _score, _time, _today, _best;
        readonly TextMeshProUGUI _resetsIn, _allDone, _streak, _streakNext, _leaderboardLabel, _shareLabel;
        readonly GameObject _challengesHeader, _streakHeader, _week;
        readonly RectTransform _challengeList;
        readonly Image[] _dayBars = new Image[7];
        readonly TextMeshProUGUI[] _dayLetters = new TextMeshProUGUI[7];
        readonly List<ChallengeRow> _rows = new List<ChallengeRow>();
        Tween _scoreTween, _shareLabelTween;
        GameOverData _data;

        sealed class ChallengeRow
        {
            public GameObject Root;
            public Image Outline;
            public TextMeshProUGUI Title, Detail, Value;
            public Button Claim;
            public TextMeshProUGUI ClaimLabel;
            public RectTransform Fill;
            public Image FillImage;
        }

        public GameOverPanel(GameObject root, TMP_Text fontSource, Button retry, Button mainMenu,
            Action<int> onClaim, Func<TimeSpan> timeUntilReset, Action onOpenLeaderboard)
        {
            _root = (RectTransform)root.transform;
            _font = fontSource != null ? fontSource.font : TMP_Settings.defaultFontAsset;
            _onClaim = onClaim;
            _timeUntilReset = timeUntilReset;

            // The scene's card, title and rich-text score are replaced by the panel below.
            foreach (Transform child in _root)
                if ((retry == null || child != retry.transform) && (mainMenu == null || child != mainMenu.transform))
                    child.gameObject.SetActive(false);

            _panel = NewRect("GameOverPanel", _root);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(PanelWidth, 0f);
            var card = _panel.gameObject.AddComponent<Image>();
            card.color = UiSkin.Surface;
            UiSprites.ApplyRounded(card, 36f);
            AddOutline(_panel, UiSkin.Frame, 36f);

            var layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 44, 28);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Result ──
            _header = AddText(_panel, "Header", 30f, GameOverLabel, TextAlignmentOptions.Center, 40f);
            _score = AddText(_panel, "Score", 120f, Color.white, TextAlignmentOptions.Center, 140f);
            AddSpace(_panel, 18f);

            // Time / today / best sit in an inset strip, darker than the panel like the score box during play.
            var stats = AddInset(_panel, "Stats", 128f, 24f);
            var statsLayout = stats.gameObject.AddComponent<HorizontalLayoutGroup>();
            statsLayout.padding = new RectOffset(0, 0, 18, 18);
            statsLayout.childControlWidth = statsLayout.childControlHeight = true;
            statsLayout.childForceExpandWidth = false;
            statsLayout.childForceExpandHeight = true;
            _time = AddStat(stats, "TIME", Color.white);
            AddDivider(stats);
            _today = AddStat(stats, "TODAY", Color.white);
            AddDivider(stats);
            _best = AddStat(stats, "BEST", UiSkin.Accent); // the number to beat
            AddSpace(_panel, 34f);

            // ── Today's challenges ──
            var challengesHeader = AddSplitRow(_panel, "ChallengesHeader", 40f,
                "TODAY'S CHALLENGES", 24f, UiSkin.TextMuted, 22f, UiSkin.TextMuted, out _resetsIn);
            _challengesHeader = challengesHeader.gameObject;
            _challengeList = NewRect("Challenges", _panel);
            var listLayout = _challengeList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 14f;
            listLayout.padding = new RectOffset(0, 0, 10, 0);
            listLayout.childControlWidth = listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            AddSpace(_panel, 10f);
            _allDone = AddText(_panel, "AllDone", 22f, UiSkin.TextMuted, TextAlignmentOptions.Center, 36f);
            AddSpace(_panel, 24f);

            // ── Streak and this week ──
            var streakHeader = AddSplitRow(_panel, "StreakHeader", 40f,
                string.Empty, 26f, UiSkin.Accent, 20f, UiSkin.TextMuted, out _streakNext);
            _streakHeader = streakHeader.gameObject;
            _streak = streakHeader.Find("Left").GetComponent<TextMeshProUGUI>();
            var week = AddInset(_panel, "Week", 92f, 18f);
            _week = week.gameObject;
            BuildWeek(week);
            AddSpace(_panel, 30f);

            // ── Actions ──
            if (retry != null) PlaceRetry(retry);
            AddSpace(_panel, 16f);
            if (mainMenu != null)
            {
                PlaceButton(mainMenu, 96f, 32f);
                var image = mainMenu.GetComponent<Image>();
                if (image != null) { image.color = UiSkin.SurfaceRaised; UiSprites.ApplyRounded(image, ButtonRadius); }
                AddOutline((RectTransform)mainMenu.transform, UiSkin.Frame, ButtonRadius);
            }
            AddSpace(_panel, 8f);
            var footer = NewRect("Footer", _panel);
            var footerElement = footer.gameObject.AddComponent<LayoutElement>();
            footerElement.preferredHeight = footerElement.minHeight = 64f;
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 16f;
            footerLayout.childControlWidth = footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = footerLayout.childForceExpandHeight = true;
            _leaderboardLabel = AddTextButton(footer, "LeaderboardLink", 64f, 24f, onOpenLeaderboard);
            _shareLabel = AddTextButton(footer, "ShareLink", 64f, 24f, ShareRun);
            _shareLabel.text = "SHARE RUN";
            SetRank(null);
        }

        public void Show(GameOverData data)
        {
            _data = data;
            _header.text = data.IsNewBest ? "NEW BEST" : "GAME OVER";
            _header.color = data.IsNewBest ? UiSkin.Accent : GameOverLabel;

            // The score counts up while the panel pops in.
            _scoreTween?.Kill();
            int shown = 0;
            _score.text = FormatNumber(0);
            _scoreTween = DOTween.To(() => shown, v => { shown = v; _score.text = FormatNumber(v); }, data.Score, 0.7f)
                .SetEase(Ease.OutCubic).SetDelay(0.15f).SetUpdate(true).SetLink(_score.gameObject);

            _time.text = FormatTime(data.SecondsSurvived);
            _today.text = FormatNumber(data.DailyBest);
            _best.text = FormatNumber(data.AllTimeHigh);

            ShowChallenges(data.Challenges, data.ChallengesBefore);
            ShowAllDone(data);
            ShowStreak(data.Streak, data.StreakRewardThisRun);
            _shareLabelTween?.Kill();
            _shareLabel.text = "SHARE RUN";
            SetRank(null); // filled in once this run's score is on the leaderboard
            FitToScreen();
        }

        /// <summary>Your All-time rank after this run's score was submitted; null until it is known.</summary>
        public void SetRank(int? rank)
        {
            _leaderboardLabel.text = rank.HasValue ? $"VIEW LEADERBOARD (#{FormatNumber(rank.Value)})" : "VIEW LEADERBOARD";
        }

        void ShareRun()
        {
            SfxPlayer.Play(Sfx.UiTap);
            if (RunShare.Share(RunShare.TextFor(_data.Score, _data.SecondsSurvived, _data.IsNewBest))) return;

            // No share sheet here (Editor, desktop): the text went to the clipboard, so say so.
            _shareLabel.text = "COPIED";
            _shareLabelTween?.Kill();
            _shareLabelTween = DOVirtual.DelayedCall(2f, () => _shareLabel.text = "SHARE RUN", ignoreTimeScale: true)
                .SetLink(_shareLabel.gameObject);
        }

        /// <summary>The line under the challenges: what finishing all of them gives, or that it's done.</summary>
        void ShowAllDone(GameOverData data)
        {
            int count = data.Challenges?.Length ?? 0;
            _allDone.gameObject.SetActive(count > 1);
            if (count <= 1) return;
            string bonus = FormatNumber(ChallengeProvider.AllDoneBonusPoints);
            if (data.AllDoneBonusThisRun)
            {
                _allDone.text = $"TODAY'S CHALLENGES COMPLETED: +{bonus} NEXT RUN";
                _allDone.color = UiSkin.Teal;
            }
            else if (data.AllDoneBonusGranted)
            {
                _allDone.text = "TODAY'S CHALLENGES COMPLETED";
                _allDone.color = UiSkin.Teal;
            }
            else
            {
                _allDone.text = $"COMPLETE ALL CHALLENGES FOR +{bonus}";
                _allDone.color = UiSkin.TextMuted;
            }
        }

        // ── Challenges ───────────────────────────────────────────────

        void ShowChallenges(ChallengeDisplayData[] now, ChallengeDisplayData[] before)
        {
            int count = now?.Length ?? 0;
            _challengesHeader.SetActive(count > 0);
            _challengeList.gameObject.SetActive(count > 0);
            while (_rows.Count < count) _rows.Add(BuildRow(_rows.Count));
            for (int i = 0; i < _rows.Count; i++) _rows[i].Root.SetActive(i < count);

            // Progress before the run only lines up when the day's challenges didn't rotate mid-run.
            bool sameSet = before != null && before.Length == count;
            for (int i = 0; i < count && sameSet; i++) sameSet = before[i].Title == now[i].Title;

            for (int i = 0; i < count; i++)
                ShowRow(_rows[i], now[i], sameSet ? before[i] : default, sameSet, i);

            UpdateResetsIn();
            DOTween.Kill(this);
            DOVirtual.DelayedCall(20f, UpdateResetsIn, ignoreTimeScale: true).SetLoops(-1).SetId(this)
                .SetLink(_panel.gameObject, LinkBehaviour.PauseOnDisablePlayOnEnable);
        }

        void UpdateResetsIn()
        {
            if (_timeUntilReset == null) { _resetsIn.text = string.Empty; return; }
            TimeSpan left = _timeUntilReset();
            int hours = (int)left.TotalHours;
            _resetsIn.text = hours > 0 ? $"Resets in {hours}h {left.Minutes}m" : $"Resets in {Mathf.Max(1, left.Minutes)}m";
        }

        void ShowRow(ChallengeRow row, ChallengeDisplayData item, ChallengeDisplayData before, bool hasBefore, int index)
        {
            row.Title.text = item.Title;
            row.Detail.text = DetailText(item, claimed: item.Claimed);
            row.Claim.onClick.RemoveAllListeners();
            row.Claim.onClick.AddListener(() => Claim(row, item, index));
            row.Claim.transform.DOKill();
            row.Claim.transform.localScale = Vector3.one;

            float to = Ratio(item);
            float from = hasBefore ? Mathf.Min(Ratio(before), to) : 0f;
            bool finishedThisRun = item.Completed && !(hasBefore && before.Completed);

            // A finished challenge reads teal (bar and border); in progress reads blue.
            SetRowDone(row, item.Completed && !finishedThisRun);
            row.Fill.DOKill();
            row.Fill.anchorMax = new Vector2(from, 1f);
            var grow = row.Fill.DOAnchorMax(new Vector2(to, 1f), 0.6f)
                .SetEase(Ease.OutCubic).SetDelay(0.35f + index * 0.12f).SetUpdate(true).SetLink(row.Root);

            if (item.Completed && !item.Claimed)
            {
                row.Value.gameObject.SetActive(false);
                row.ClaimLabel.text = $"CLAIM +{FormatNumber(item.RewardPoints)}";
                row.Claim.gameObject.SetActive(true);
                if (finishedThisRun)
                {
                    // Finished in this run: the bar fills first, then the claim button arrives.
                    row.Claim.transform.localScale = Vector3.zero;
                    grow.OnComplete(() =>
                    {
                        SetRowDone(row, true);
                        row.Claim.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(row.Root);
                    });
                }
            }
            else
            {
                row.Claim.gameObject.SetActive(false);
                row.Value.gameObject.SetActive(true);
                row.Value.text = item.Completed
                    ? "CLAIMED"
                    : $"{FormatChallengeValue(item.Type, item.Progress)} / {FormatChallengeValue(item.Type, item.TargetValue)}";
                row.Value.color = item.Completed ? UiSkin.Teal : UiSkin.TextMuted;
                if (finishedThisRun) grow.OnComplete(() => SetRowDone(row, true));
            }
        }

        // Once claimed, the amount moves to the right ("+1,000 NEXT RUN", later "CLAIMED"); keeping it on this line
        // too made claimed rows look like they still had points to collect.
        static string DetailText(ChallengeDisplayData item, bool claimed) => claimed
            ? ChallengeDifficulty.Label(item.Difficulty)
            : $"{ChallengeDifficulty.Label(item.Difficulty)}  <color=#F5C04A>+{FormatNumber(item.RewardPoints)}</color>";

        static void SetRowDone(ChallengeRow row, bool done)
        {
            row.FillImage.color = done ? UiSkin.Teal : UiSkin.Primary;
            row.Outline.color = done ? new Color(UiSkin.Teal.r, UiSkin.Teal.g, UiSkin.Teal.b, 0.55f) : UiSkin.Frame;
        }

        void Claim(ChallengeRow row, ChallengeDisplayData item, int index)
        {
            _onClaim?.Invoke(index);
            SfxPlayer.Play(Sfx.UiTap);
            row.Claim.gameObject.SetActive(false);
            row.Detail.text = DetailText(item, claimed: true);
            row.Value.gameObject.SetActive(true);
            row.Value.color = UiSkin.Teal;
            row.Value.text = $"+{FormatNumber(item.RewardPoints)} NEXT RUN"; // claimed points start the next run
        }

        ChallengeRow BuildRow(int index)
        {
            var row = new ChallengeRow();
            var rect = NewRect("Challenge" + index, _challengeList);
            row.Root = rect.gameObject;
            var element = row.Root.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = RowHeight;
            var bg = row.Root.AddComponent<Image>();
            bg.color = UiSkin.SurfaceRaised;
            bg.raycastTarget = false;
            UiSprites.ApplyRounded(bg, 20f);
            row.Outline = AddOutline(rect, UiSkin.Frame, 20f);

            var titleRect = NewRect("Title", rect);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(24f, -56f);
            titleRect.offsetMax = new Vector2(-(24f + RowValueWidth), -12f);
            row.Title = NewText(titleRect, 30f, Color.white, TextAlignmentOptions.MidlineLeft);
            row.Title.overflowMode = TextOverflowModes.Ellipsis;

            // Difficulty and reward under the title.
            var detailRect = NewRect("Detail", rect);
            detailRect.anchorMin = new Vector2(0f, 1f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.pivot = new Vector2(0f, 1f);
            detailRect.offsetMin = new Vector2(24f, -86f);
            detailRect.offsetMax = new Vector2(-(24f + RowValueWidth), -56f);
            row.Detail = NewText(detailRect, 20f, UiSkin.TextMuted, TextAlignmentOptions.MidlineLeft);

            var valueRect = NewRect("Value", rect);
            valueRect.anchorMin = valueRect.anchorMax = valueRect.pivot = new Vector2(1f, 1f);
            valueRect.sizeDelta = new Vector2(RowValueWidth, 44f);
            valueRect.anchoredPosition = new Vector2(-24f, -12f);
            row.Value = NewText(valueRect, 26f, UiSkin.TextMuted, TextAlignmentOptions.MidlineRight);

            var claimRect = NewRect("ClaimBtn", rect);
            claimRect.anchorMin = claimRect.anchorMax = claimRect.pivot = new Vector2(1f, 1f);
            claimRect.sizeDelta = new Vector2(228f, 56f);
            claimRect.anchoredPosition = new Vector2(-18f, -10f);
            var claimImage = claimRect.gameObject.AddComponent<Image>();
            claimImage.color = UiSkin.Accent;
            UiSprites.ApplyRounded(claimImage, 16f);
            row.Claim = claimRect.gameObject.AddComponent<Button>();
            row.Claim.targetGraphic = claimImage;
            var claimLabelRect = NewRect("Text", claimRect);
            Stretch(claimLabelRect);
            row.ClaimLabel = NewText(claimLabelRect, 24f, DarkLabel, TextAlignmentOptions.Center);
            UiSkin.PadLabels(row.Claim);

            var track = NewRect("Track", rect);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.offsetMin = new Vector2(24f, 18f);
            track.offsetMax = new Vector2(-24f, 30f);
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = UiSkin.Board;
            trackImage.raycastTarget = false;
            UiSprites.ApplyRounded(trackImage, 6f);

            row.Fill = NewRect("Fill", track);
            row.Fill.anchorMin = Vector2.zero;
            row.Fill.anchorMax = new Vector2(0f, 1f);
            row.Fill.offsetMin = row.Fill.offsetMax = Vector2.zero;
            row.FillImage = row.Fill.gameObject.AddComponent<Image>();
            row.FillImage.raycastTarget = false;
            UiSprites.ApplyRounded(row.FillImage, 6f);
            return row;
        }

        // ── Streak ───────────────────────────────────────────────────

        /// <summary>
        /// The streak as a countdown to its next 7th day, which adds a bonus to the next run (day 7, 14, 21..., growing
        /// each week; see <see cref="StreakReward"/>). The seventh bar is tinted gold until it's reached.
        /// </summary>
        void ShowStreak(int streak, bool rewardThisRun)
        {
            bool show = streak > 0;
            _streakHeader.SetActive(show);
            _week.SetActive(show);
            if (!show) return;

            int day = StreakReward.DayInCycle(streak); // 1-7
            string reward = FormatNumber(StreakReward.NextPoints(streak));
            _streak.text = $"{streak}-DAY STREAK";
            if (day == StreakReward.Days)
            {
                _streakNext.text = rewardThisRun ? $"+{reward} NEXT RUN" : $"+{reward} EARNED TODAY";
                _streakNext.color = UiSkin.Teal;
            }
            else
            {
                int left = StreakReward.Days - day;
                _streakNext.text = left == 1 ? $"TOMORROW: +{reward}" : $"{left} DAYS TO +{reward}";
                _streakNext.color = UiSkin.TextMuted;
            }

            var rewardHint = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.35f);
            for (int i = 0; i < 7; i++)
            {
                bool reached = i < day;
                bool isToday = i == day - 1;
                bool rewardDay = i == 6;
                _dayBars[i].color = reached ? (rewardDay ? UiSkin.Teal : UiSkin.Accent) : rewardDay ? rewardHint : UiSkin.SurfaceRaised;
                var bar = _dayBars[i].rectTransform;
                bar.sizeDelta = new Vector2(bar.sizeDelta.x, isToday ? 16f : 12f);
                _dayLetters[i].color = isToday ? UiSkin.Accent : UiSkin.TextMuted;
            }
        }

        void BuildWeek(RectTransform week)
        {
            var row = week.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 18, 16, 10);
            row.spacing = 12f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            for (int i = 0; i < 7; i++)
            {
                var day = NewRect("Day" + DayNumbers[i], week);
                var barRect = NewRect("Bar", day);
                barRect.anchorMin = new Vector2(0f, 1f);
                barRect.anchorMax = new Vector2(1f, 1f);
                barRect.pivot = new Vector2(0.5f, 1f);
                barRect.anchoredPosition = new Vector2(0f, -4f);
                barRect.sizeDelta = new Vector2(0f, 12f);
                _dayBars[i] = barRect.gameObject.AddComponent<Image>();
                _dayBars[i].raycastTarget = false;
                UiSprites.ApplyRounded(_dayBars[i], 6f);

                var letterRect = NewRect("Letter", day);
                letterRect.anchorMin = Vector2.zero;
                letterRect.anchorMax = new Vector2(1f, 0f);
                letterRect.pivot = new Vector2(0.5f, 0f);
                letterRect.sizeDelta = new Vector2(0f, 32f);
                _dayLetters[i] = NewText(letterRect, 22f, UiSkin.TextMuted, TextAlignmentOptions.Center);
                _dayLetters[i].text = DayNumbers[i];
            }
        }

        // ── Layout helpers ───────────────────────────────────────────

        /// <summary>Panel width follows the screen; on short screens (tablets) the whole panel scales down to fit.</summary>
        void FitToScreen()
        {
            Rect area = _root.rect;
            _panel.sizeDelta = new Vector2(Mathf.Min(PanelWidth, area.width - 2f * ScreenMargin), _panel.sizeDelta.y);
            _panel.localScale = Vector3.one;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            float height = LayoutUtility.GetPreferredHeight(_panel);
            float available = area.height - 2f * ScreenMargin;
            if (height > available && height > 0f) _panel.localScale = Vector3.one * (available / height);
        }

        void PlaceRetry(Button retry)
        {
            PlaceButton(retry, RetryHeight, 40f);
            var label = retry.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "PLAY AGAIN"; // starts a new run (the scene's label said RETRY)
            var image = retry.GetComponent<Image>();
            if (image != null) { image.color = UiSkin.Primary; UiSprites.ApplyRounded(image, ButtonRadius); }
        }

        void PlaceButton(Button button, float height, float fontSize)
        {
            button.transform.SetParent(_panel, false);
            button.transform.SetAsLastSibling();
            if (!button.TryGetComponent(out LayoutElement element)) element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = Color.white;
                label.fontSize = fontSize;
                label.fontSizeMax = fontSize;
            }
        }

        TextMeshProUGUI AddTextButton(RectTransform parent, string name, float height, float size, Action onClick)
        {
            var rect = NewRect(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f); // invisible, only gives the link its tap area
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick?.Invoke());
            var labelRect = NewRect("Text", rect);
            Stretch(labelRect);
            var label = NewText(labelRect, size, UiSkin.TextMuted, TextAlignmentOptions.Center);
            UiSkin.PadLabels(button);
            return label;
        }

        RectTransform AddInset(RectTransform parent, string name, float height, float radius)
        {
            var rect = NewRect(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = UiSkin.Board;
            image.raycastTarget = false;
            UiSprites.ApplyRounded(image, radius);
            return rect;
        }

        /// <summary>A row with a label on the left and a smaller note on the right (the right text is returned).</summary>
        RectTransform AddSplitRow(RectTransform parent, string name, float height, string left, float leftSize, Color leftColor,
            float rightSize, Color rightColor, out TextMeshProUGUI right)
        {
            var rect = NewRect(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;

            var leftRect = NewRect("Left", rect);
            leftRect.anchorMin = Vector2.zero;
            leftRect.anchorMax = new Vector2(0.55f, 1f);
            leftRect.offsetMin = leftRect.offsetMax = Vector2.zero;
            NewText(leftRect, leftSize, leftColor, TextAlignmentOptions.MidlineLeft).text = left;

            var rightRect = NewRect("Right", rect);
            rightRect.anchorMin = new Vector2(0.55f, 0f);
            rightRect.anchorMax = Vector2.one;
            rightRect.offsetMin = rightRect.offsetMax = Vector2.zero;
            right = NewText(rightRect, rightSize, rightColor, TextAlignmentOptions.MidlineRight);
            return rect;
        }

        TextMeshProUGUI AddStat(RectTransform parent, string caption, Color valueColor)
        {
            var cell = NewRect(caption, parent);
            cell.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var captionRect = NewRect("Caption", cell);
            captionRect.anchorMin = new Vector2(0f, 0.6f);
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = captionRect.offsetMax = Vector2.zero;
            NewText(captionRect, 22f, UiSkin.TextMuted, TextAlignmentOptions.Center).text = caption;

            var valueRect = NewRect("Value", cell);
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = new Vector2(1f, 0.6f);
            valueRect.offsetMin = valueRect.offsetMax = Vector2.zero;
            return NewText(valueRect, 42f, valueColor, TextAlignmentOptions.Center);
        }

        /// <summary>Hairline between two stats.</summary>
        void AddDivider(RectTransform parent)
        {
            var divider = NewRect("Divider", parent);
            var element = divider.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = 2f;
            element.flexibleWidth = 0f;
            var image = divider.gameObject.AddComponent<Image>();
            image.color = UiSkin.Frame;
            image.raycastTarget = false;
        }

        /// <summary>Hairline border drawn over a rounded rect (ignored by its layout).</summary>
        static Image AddOutline(RectTransform target, Color color, float radius)
        {
            var rect = NewRect("Border", target);
            Stretch(rect);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            UiSprites.ApplyRoundedOutline(image, radius);
            return image;
        }

        TextMeshProUGUI AddText(RectTransform parent, string name, float size, Color color, TextAlignmentOptions alignment, float height)
        {
            var rect = NewRect(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;
            return NewText(rect, size, color, alignment);
        }

        void AddSpace(RectTransform parent, float height)
        {
            var element = NewRect("Space", parent).gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = height;
        }

        TextMeshProUGUI NewText(RectTransform rect, float size, Color color, TextAlignmentOptions alignment)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontStyle = FontStyles.Normal; // the game's font asset is already the bold cut
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = Mathf.Max(14f, size * 0.6f);
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static float Ratio(ChallengeDisplayData item) =>
            item.Completed ? 1f : item.TargetValue > 0 ? Mathf.Clamp01((float)item.Progress / item.TargetValue) : 0f;

        static string FormatNumber(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>m:ss, or h:mm:ss past an hour.</summary>
        public static string FormatTime(float seconds)
        {
            var span = TimeSpan.FromSeconds(Mathf.Max(0f, Mathf.Floor(seconds)));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes}:{span.Seconds:00}";
        }

        /// <summary>A challenge's progress or target the way the player reads it: points, a time, or a combo.</summary>
        public static string FormatChallengeValue(ChallengeType type, int value) => type switch
        {
            ChallengeType.SurviveSeconds => FormatTime(value),
            ChallengeType.MaxComboMultiplier => $"x{value}",
            _ => FormatNumber(value),
        };
    }
}
