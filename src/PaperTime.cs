using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskMate
{
    internal sealed class PaperTime
    {
        internal string Key, Error, RepeatKey;
        internal long Utc;

        const string Digits = "0-9零〇一二两三四五六七八九十百千";
        const string N = "[" + Digits + "]{1,10}";
        const string Period = @"凌晨|清晨|早上|早晨|上午|中午|下午|傍晚|晚上|夜里|早(?=\s*[" + Digits + @"])|晚(?=\s*[" + Digits + @"])";
        const string Clock = @"(?<hour>" + N + @")\s*(?::\s*(?<colon>[0-9]+)|[点时](?:钟)?\s*(?:(?<halfClock>半)|(?<quarter>[一二两三])刻|整|(?<minute>" + N + @")分?)?)";
        const string Weekday = @"(?<week>(?:下下|这个|这|本|下|上)?(?:周|星期|礼拜))\s*(?<weekday>[一二三四五六日天1-7])";
        const string Calendar = @"(?:(?<year>" + N + @")年\s*|(?<yearWord>今年|明年|后年)\s*)?(?<month>" + N + @")月\s*(?<day>" + N + @")[日号]";
        const string IsoDate = @"(?<year>[0-9]{4})(?<sep>[-/.])(?<month>[0-9]{1,2})\k<sep>(?<day>[0-9]{1,2})(?![0-9])";
        const string DayOfMonth = @"(?:(?<monthWord>下下个?月|下个?月|这个?月|本月|上个?月)\s*)?(?<monthDay>" + N + @")[号日]";
        const string Day = @"(?<date>" + Weekday + "|" + Calendar + "|" + IsoDate + "|" + DayOfMonth + "|大后天|后天|明天|今天|昨天|前天)";
        const string ShortDuration = @"(?:(?<half>半)\s*个?\s*(?:小时|钟头)|(?<hours>" + N + @")\s*个?\s*(?:(?<half>半)\s*(?:小时|钟头)|" +
            @"(?:小时|钟头)(?:\s*(?<half>半)|\s*零?(?<minutes>" + N + @")\s*分钟)?)|(?<minutes>" + N + @")\s*分钟|(?<quarters>[一二两三1-3])刻钟)";
        const string DayDuration = @"(?<days>" + N + @")\s*天(?:\s*(?:零|又)?\s*" + ShortDuration + ")?";
        const string Duration = @"(?<duration>(?<weeks>" + N + @")\s*个?\s*(?:星期|周)(?:\s*(?:零|又)?\s*(?:" +
            DayDuration + "|" + ShortDuration + "))?|" + DayDuration + "|" + ShortDuration + @")\s*(?<after>以后|之后|后|以前|之前|前)?";
        static readonly Regex Tokens = Pattern("(?<![" + Digits + "])(?:" +
            Day + @"(?:\s*[,，]?\s*(?:请)?(?:提醒我|叫我|记得)?(?:在|到)?\s*(?<period>" + Period + @")?\s*" + Clock +
            @")?(?:\s*(?<periodOnly>" + Period + "))?|" +
            "(?:(?<period>" + Period + @")\s*)?" + Clock + "|" + Duration + ")");
        static readonly Regex Reminder = Pattern("提醒(?!事项|功能|方式|设置|声音|铃声|图标|按钮|列表)|闹钟(?!铃声|功能|图标)|叫醒|叫我|通知我");
        static readonly Regex BeforeDuration = Pattern(@"(?:提醒我?|倒计时|计时|再过|(?:^|[\s,，。；！?？、]|请|你)过)\s*[：:,，]?\s*$");
        static readonly Regex AfterDuration = Pattern(@"^\s*(?:的)?(?:再)?(?:提醒|倒计时|计时|闹钟|叫我|通知我)");
        static readonly Regex Repeated = Pattern(@"每(?:隔|逢|个?星期|个?礼拜|个?周|天|日|晚|早|月|年|小时|分钟)|(?:每(?:过)?|隔)\s*(?:半|[" + Digits + @"]+)\s*个?半?\s*(?:天|小时|分钟|钟头|星期|周)|工作日|隔天|隔日");
        static readonly Regex RepeatRule = Pattern(
            @"(?<cycle>每(?:个)?(?:工作日|(?:周|星期|礼拜)一(?:到|至)(?:周|星期|礼拜)?五)|(?:每(?:个|逢)?)(?:周|星期|礼拜)(?<on>[一二三四五六日天1-7])|每天|每日|每晚|每早)" +
            @"\s*(?:[,，]?\s*(?:请)?(?:提醒我|叫我|记得)?(?:在|到)?\s*)(?<period>" + Period + @")?\s*" + Clock);
        // Bounded or alternate-week schedules cannot be represented by a weekly day mask.
        static readonly Regex LimitedRepeat = Pattern(@"(?:截止|截至|直到|最后一次|到期为止)|" +
            @"(?:持续|连续|一连|共计?|一共|为期|只(?:提醒)?|限)\s*" + N + @"\s*个?\s*(?:天|周|礼拜|星期|月|年|次)|" +
            N + @"\s*个?\s*(?:周|礼拜|星期|天|月|年)\s*(?:提醒)?\s*(?:一次|一回)");
        static readonly Regex Negated = Pattern(@"(?:不要|不用|别再?|取消|撤销|不必|不需要|无需|不想要?)\s*(?:再|这个|设置|给我|帮我)?\s*(?:提醒|通知|叫我|闹钟)");
        static readonly Regex Vague = Pattern(@"待会儿?|等会儿?|过会儿?|一会儿?|过一阵|过阵子|过段时间|回头再|稍后|晚点|改天|周末|月底|月初|明后天|几(?:分钟|小时|天|点钟)|有空时");
        static readonly Regex BeforeAmbiguous = Pattern(@"(?:不是|不要|不在|别在|大约|大概|差不多|不晚于|不早于|最晚|最早|提前|延后|前|(?:[" + Digits + @"]\s*(?:到|至|或|~|～|-))|(?:本|这|下下?|上)(?:周|星期|礼拜|个?月)|明年|今年|后年|[" + Digits + @"](?:年|月|日|号)|[0-9]+[-/.][0-9]+(?:[-/.][0-9]+)?|[-−.])\s*(?:在)?\s*$");
        static readonly Regex AfterAmbiguous = Pattern(@"^\s*(?:左右|上下|前后|以前|之前|之后|以后|以内|之内|内|前(?!台|来|往|门|院|排)|后(?!台|门|院|排)|多(?:分|钟|秒)?|(?:到|差)\s*(?:半|[" + Digits + @"])|或(?:者)?\s*(?:半|[" + Digits + @"])|[:刻秒]|[aApP]\.?[mM]\b)");
        // If the clock is recognizable but its calendar qualifier isn't, don't apply today's date.
        const string CalendarHint = @"(?:明年|今年|后年|去年|农历|阴历|生日|纪念日|元旦|除夕|春节|清明|五一|端午|中秋|国庆|圣诞|假期|放假|开学|节日|[^\s，。]节|下周|上周|下个月|上个月)";
        static readonly Regex UnreadCalendar = Pattern(CalendarHint);
        static readonly Regex AdjacentCalendar = Pattern(CalendarHint + @"(?:那天|这天|当天|当日|期间|的|在|\s)*$");
        static readonly Regex DurationJoin = Pattern(@"^\s*(?:零|又|加|加上)?\s*$");
        static readonly Regex ExactEnd = Pattern(@"^[\s。.!！?？，,;；]*$");
        static readonly Regex NonClockNoun = Pattern(@"^\s*(?:建议|意见|想法|理由|原因|区别|差异|共识|要求|要点|注意事项|原则|要说|要讲|要补充|如下|说明|折|线)");
        static readonly Regex SmallAmount = Pattern(@"^\s*(?:也|都|小|东西|吃的|喝的|钱|水|茶|牛奶|儿|点|心意|时间|力气|耐心|勇气|经验|资料)");

        static Regex Pattern(string pattern)
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80));
        }
        static PaperTime Ask(string question) { return new PaperTime { Error = question }; }
        static PaperTime Unclear() { return Ask("请写一个确定的提醒时间，例如「明天下午三点」"); }
        static bool FigurativeClock(Match token, string before, string after)
        {
            if (!token.Groups["hour"].Success || token.Groups["date"].Success || token.Groups["period"].Success ||
                token.Groups["colon"].Success) return false;
            if (before.EndsWith("第",StringComparison.Ordinal) || NonClockNoun.IsMatch(after)) return true;
            // The repeat token also contains the prefix (e.g. 每天). Inspect just its clock.
            switch (token.Value.Substring(token.Groups["hour"].Index-token.Index).TrimEnd())
            {
                case "一点": case "1点": return SmallAmount.IsMatch(after);
                case "一时": return after.StartsWith("间") || after.StartsWith("冲动");
                case "一时半": return after.StartsWith("会");
                default: return false;
            }
        }
        static int Number(string text, bool year = false)
        {
            int n;
            if (Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return n;
            if (String.IsNullOrEmpty(text)) return -1;
            const string chinese = "零一二三四五六七八九";
            string value = text.Replace('两', '二').Replace('〇', '零');
            if (value.IndexOfAny(new[]{'十','百','千'}) < 0)
            {
                // 二〇二八 is an ordinary year; 三五分钟 usually means an estimate.
                if (!year && value.Length > 1 && value[0] != '零') return -1;
                n = 0;
                foreach (char c in value)
                {
                    int digit = chinese.IndexOf(c);
                    if (digit < 0 || n > 9999) return -1;
                    n = n * 10 + digit;
                }
                return n;
            }
            int total = 0, current = -1, previousUnit = 10000;
            foreach (char c in value)
            {
                int digit = chinese.IndexOf(c);
                if (digit >= 0)
                {
                    if (current > 0) return -1;
                    current = digit;
                    continue;
                }
                int unit = c == '千' ? 1000 : c == '百' ? 100 : c == '十' ? 10 : 0;
                if (unit == 0 || unit >= previousUnit || current == 0) return -1;
                total += unit * (current < 0 ? 1 : current);
                previousUnit = unit;
                current = -1;
            }
            return total + Math.Max(current,0);
        }
        internal static PaperTime Read(string text, DateTime now)
        {
            if (String.IsNullOrWhiteSpace(text)) return new PaperTime();
            if (text.Length > 16384) return Ask("文字太长，请把提醒时间写在一张短便签上");
            try
            {
                string value = text.Normalize(NormalizationForm.FormKC).Trim()
                    .Replace("明早上","明天早上").Replace("明晚上","明天晚上")
                    .Replace("明早","明天早上").Replace("明晚","明天晚上")
                    .Replace("今早","今天早上").Replace("今晚","今天晚上");
                return ReadTokens(value, now);
            }
            catch (RegexMatchTimeoutException) { return Unclear(); }
            catch (ArgumentException) { return Unclear(); }
        }
        static PaperTime ReadTokens(string value, DateTime now)
        {
            bool reminder = Reminder.IsMatch(value);
            Match first = Tokens.Match(value);
            bool hasTokens = first.Success;
            if ((hasTokens || reminder) && Negated.IsMatch(value)) return Ask("这句话写了取消提醒，请删去时间或改写提醒要求");
            if ((hasTokens || reminder) && Repeated.IsMatch(value)) return ReadRepeat(value, now);
            Match vague = Vague.Match(value);
            if (vague.Success && (hasTokens || reminder || vague.Index == 0)) return Unclear();
            PaperTime result = null;
            bool detachedDate = false;
            int durationEnd = -1;
            for (Match token = first; token.Success; token = token.NextMatch())
            {
                int end = token.Index + token.Length;
                int start = Math.Max(0,token.Index - 48);
                string before = value.Substring(start,token.Index - start);
                string after = value.Substring(end,Math.Min(48,value.Length-end));
                if (FigurativeClock(token,before,after)) continue;
                if (BeforeAmbiguous.IsMatch(before) || AfterAmbiguous.IsMatch(after)) return Unclear();
                if (token.Groups["duration"].Success)
                {
                    if (durationEnd >= 0 && token.Index-durationEnd <= 16 &&
                        DurationJoin.IsMatch(value.Substring(durationEnd,token.Index-durationEnd))) return Unclear();
                    durationEnd = end;
                    string suffix = token.Groups["after"].Value;
                    if (suffix.EndsWith("前",StringComparison.Ordinal)) return Ask("这个提醒时间已经过去了");
                    if (suffix.Length == 0 && !BeforeDuration.IsMatch(before) && !AfterDuration.IsMatch(after) &&
                        !(start == 0 && end + after.Length == value.Length && ExactEnd.IsMatch(before) && ExactEnd.IsMatch(after))) continue;
                    if (result != null || detachedDate) return Multiple();
                    result = Relative(token.Groups,now);
                }
                else if (!token.Groups["hour"].Success)
                {
                    if (reminder || (token.Groups["date"].Value.EndsWith("天") && token.Groups["date"].Value != "今天") ||
                        token.Groups["periodOnly"].Success || token.Groups["month"].Success || token.Groups["monthDay"].Success ||
                        token.Groups["week"].Success || (ExactEnd.IsMatch(before) && ExactEnd.IsMatch(after)))
                        return Ask("请补上具体钟点，例如「明天下午三点」");
                    detachedDate = true;
                    if (result != null) return Multiple();
                }
                else
                {
                    if (result != null || detachedDate) return Multiple();
                    if ((token.Groups["date"].Success ? AdjacentCalendar : UnreadCalendar).IsMatch(before)) return Unclear();
                    result = WallTime(token.Groups, now);
                }
                if (result != null && result.Error != null && result.Key == null) return result;
            }
            return result ?? (reminder ? Unclear() : new PaperTime());
        }
        static PaperTime Multiple() { return Ask("这张便签有多个时间，请只保留一个提醒时间"); }
        static PaperTime Relative(GroupCollection tags, DateTime now)
        {
            long total = tags["half"].Success ? 30 : 0;
            string[] units = { "minutes", "quarters", "hours", "days", "weeks" };
            int[] scales = { 1, 15, 60, 1440, 10080 };
            for (int i = 0; i < units.Length; i++)
            {
                if (!tags[units[i]].Success) continue;
                int amount = Number(tags[units[i]].Value);
                if (amount < 0) return Unclear();
                total += (long)amount * scales[i];
            }
            if (total < 1 || total > 10080) return Ask("提醒可设在 1 分钟到 7 天后");
            return new PaperTime { Key = "+" + total, Utc = now.ToUniversalTime().AddMinutes(total).Ticks };
        }
        // A repeating note stores the local wall clock and a bit for each weekday.
        // Derive the next future date, never one notification per missed occurrence.
        internal const int EveryDay=127, MondayToFriday=62;
        internal static string RepeatId(int days, int minute)
        {
            return days.ToString(CultureInfo.InvariantCulture)+"/"+minute.ToString(CultureInfo.InvariantCulture);
        }
        static bool RepeatParts(string key, out int days, out int minute)
        {
            days=minute=0;
            if(String.IsNullOrEmpty(key))return false;
            var fields=key.Split('/');
            return fields.Length==2 && Int32.TryParse(fields[0],NumberStyles.None,CultureInfo.InvariantCulture,out days) &&
                Int32.TryParse(fields[1],NumberStyles.None,CultureInfo.InvariantCulture,out minute) &&
                (days==EveryDay || days==MondayToFriday || days==124 || (days>0 && days<=64 && (days&(days-1))==0)) && minute>=0 && minute<1440;
        }
        internal static string RepeatLabel(string key)
        {
            int days,minute;
            if(!RepeatParts(key,out days,out minute))return "";
            if(days==EveryDay)return "每天";
            if(days==MondayToFriday)return "周一至周五";
            if(days==124)return "周二至周六";
            for(int i=0;i<7;i++)if((days&(1<<i))!=0)return "每周"+"日一二三四五六"[i];
            return "";
        }
        internal static long NextRepeat(string key,DateTime now)
        {
            int days,minute;
            if(!RepeatParts(key,out days,out minute))return 0;
            for(int offset=0;offset<=14;offset++){
                var day=now.Date.AddDays(offset);
                if((days&(1<<(int)day.DayOfWeek))==0)continue;
                DateTime local=day.AddMinutes(minute);
                if(!TimeZoneInfo.Local.IsInvalidTime(local) && local.ToUniversalTime()>now.ToUniversalTime())
                    return local.ToUniversalTime().Ticks;
            }
            return 0;
        }
        static PaperTime ReadRepeat(string value, DateTime now)
        {
            var match=RepeatRule.Match(value);
            if(!match.Success)return Ask("可写「每周五下午四点」或在小钟中选择提醒频率");
            string before=value.Substring(0,match.Index),after=value.Substring(match.Index+match.Length);
            if(FigurativeClock(match,before,after))
                return Reminder.IsMatch(value) || OtherDeadline(before) || OtherDeadline(after) ? Unclear() : new PaperTime();
            if(Repeated.IsMatch(before) || Repeated.IsMatch(after) || LimitedRepeat.IsMatch(value) || OtherDeadline(before) || OtherDeadline(after) ||
                Vague.IsMatch(value) || UnreadCalendar.IsMatch(before) || UnreadCalendar.IsMatch(after) ||
                BeforeAmbiguous.IsMatch(before) || AfterAmbiguous.IsMatch(after) ||
                Regex.IsMatch(before,@"(?:只|仅|除了?|跳过|取消|不要|不用|不在|并非|不是|从)\s*$"))
                return Ask("请确认重复提醒的日期和钟点");
            string cycle=match.Groups["cycle"].Value,period=match.Groups["period"].Value;
            if(cycle=="每早" || cycle=="每晚"){
                if(period.Length>0)return Unclear();
                period=cycle=="每晚"?"晚上":"早上";
            }
            TimeSpan clock;
            if(!ClockTime(match.Groups,period,out clock))return Unclear();
            int days=EveryDay;
            if(match.Groups["on"].Success){
                string digit=match.Groups["on"].Value;
                int weekday=(digit=="日" || digit=="天")?0:Number(digit)%7;
                days=1<<weekday;
            } else if(cycle.Contains("工作日") || cycle.Contains("五"))days=MondayToFriday;
            if(clock.Days>0)days=((days<<1)&127)|(days>>6);
            string repeat=RepeatId(days,clock.Hours*60+clock.Minutes);
            long utc=NextRepeat(repeat,now);
            if(utc==0)return Ask("请确认重复提醒的当地时间");
            return new PaperTime{Key="repeat:"+repeat,RepeatKey=repeat,Utc=utc};
        }
        static bool OtherDeadline(string fragment)
        {
            for(var token=Tokens.Match(fragment); token.Success; token=token.NextMatch()){
                int start=Math.Max(0,token.Index-48), end=token.Index+token.Length;
                string before=fragment.Substring(start,token.Index-start), after=fragment.Substring(end,Math.Min(48,fragment.Length-end));
                if(FigurativeClock(token,before,after))continue;
                if(!token.Groups["duration"].Success || token.Groups["after"].Success ||
                    BeforeDuration.IsMatch(before) || AfterDuration.IsMatch(after))return true;
            }
            return false;
        }
        static bool ClockTime(GroupCollection tags,string period,out TimeSpan clock)
        {
            clock=TimeSpan.Zero;
            int hour = Number(tags["hour"].Value), minute = 0;
            if (tags["halfClock"].Success) minute = 30;
            if (tags["quarter"].Success) minute = 15 * Number(tags["quarter"].Value);
            if (tags["minute"].Success) minute = Number(tags["minute"].Value);
            if (tags["colon"].Success) minute = tags["colon"].Length > 2 ? -1 : Number(tags["colon"].Value);
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || (period.Length != 0 && hour > 12))
                return false;
            bool late = period == "晚上" || period == "夜里" || period == "晚";
            bool midnight = late && hour == 12;
            if (midnight || ((period == "凌晨" || period == "早上" || period == "清晨" || period == "早晨" || period == "上午" || period == "早") && hour == 12)) hour = 0;
            else if ((late || period == "下午" || period == "傍晚" || (period == "中午" && hour <= 3)) && hour < 12) hour += 12;

            clock=new TimeSpan(midnight?1:0,hour,minute,0);
            return true;
        }
        static PaperTime WallTime(GroupCollection tags, DateTime now)
        {
            string date=tags["date"].Value;
            TimeSpan clock;
            if(!ClockTime(tags,tags["period"].Value,out clock))return Ask("请检查小时和分钟，例如「下午三点」或「15:30」");
            bool midnight=clock.Days>0;
            int hour=clock.Hours,minute=clock.Minutes;
            string dayKey = date;
            DateTime due;
            if (tags["month"].Success)
            {
                int month = Number(tags["month"].Value), day = Number(tags["day"].Value);
                string yearWord = tags["yearWord"].Value;
                bool specificYear = tags["year"].Success || yearWord.Length > 0;
                int year = tags["year"].Success ? Number(tags["year"].Value,true) :
                    now.Year + (yearWord == "明年" ? 1 : yearWord == "后年" ? 2 : 0);
                if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2000,month))
                    return Ask("这个日期不存在，请检查年月日");
                if (specificYear && day > DateTime.DaysInMonth(year,month)) return Ask("这个日期不存在，请检查年月日");
                for (;;year++)
                {
                    if (day > DateTime.DaysInMonth(year,month)) continue;
                    due = new DateTime(year,month,day,0,0,0,now.Kind) + clock;
                    if (specificYear || due > now) break;
                }
                dayKey = (tags["year"].Success ? year.ToString(CultureInfo.InvariantCulture) : yearWord) + "/" + month + "/" + day;
            }
            else if (tags["monthDay"].Success)
            {
                int day = Number(tags["monthDay"].Value);
                string word = tags["monthWord"].Value;
                int offset = word.StartsWith("下下") ? 2 : word.StartsWith("下") ? 1 : word.StartsWith("上") ? -1 : 0;
                DateTime month = new DateTime(now.Year,now.Month,1,0,0,0,now.Kind).AddMonths(offset);
                if (day < 1 || day > 31) return Ask("这个月份的日期不存在，请检查日号");
                for (;;month = month.AddMonths(1))
                {
                    if (day > DateTime.DaysInMonth(month.Year,month.Month))
                    {
                        if (word.Length > 0) return Ask("这个月份的日期不存在，请检查日号");
                        continue;
                    }
                    due = month.AddDays(day-1) + clock;
                    if (word.Length > 0 || due > now) break;
                }
                dayKey = (word.Length > 0 ? "月" + offset : "月") + "/" + day;
            }
            else if (tags["week"].Success)
            {
                string week = tags["week"].Value;
                string digit = tags["weekday"].Value;
                int weekday = digit == "日" || digit == "天" ? 7 : Number(digit);
                int offset = week.StartsWith("下下") ? 2 : week.StartsWith("下") ? 1 : week.StartsWith("上") ? -1 : 0;
                bool nearest = week == "周" || week == "星期" || week == "礼拜";
                int today = ((int)now.DayOfWeek + 6) % 7 + 1;
                due = now.Date.AddDays(weekday - today + 7 * offset) + clock;
                if (nearest && due <= now) due = due.AddDays(7);
                dayKey = (nearest ? "周" : "周" + offset + "/") + weekday;
            }
            else
            {
                int offset = date == "明天" ? 1 : date == "后天" ? 2 : date == "大后天" ? 3 :
                    date == "昨天" ? -1 : date == "前天" ? -2 : 0;
                due = now.Date.AddDays(offset) + clock;
                if (date.Length == 0 && due <= now) due = due.AddDays(1);
            }
            // Persist the instruction, not today's resolution, so editing the body preserves a delivered alarm.
            var result = new PaperTime { Key = dayKey + (midnight ? "+1" : "") + "@" + hour + ":" + minute,
                Utc = due.ToUniversalTime().Ticks };
            if (due <= now) result.Error = "这个提醒时间已经过去了";
            return result;
        }
    }
}
