using System;
using System.Diagnostics;
using DeskMate;

internal static class PaperTimeChecks
{
    static readonly DateTime Friday = new DateTime(2026, 11, 27, 10, 17, 0, DateTimeKind.Local);
    static int count;

    static void Expect(string text, DateTime due, string key = null)
    {
        var time = PaperTime.Read(text, Friday);
        if (time.Error != null || time.Key == null || time.Utc != due.ToUniversalTime().Ticks || (key != null && time.Key != key))
            throw new Exception(text + " => " + time.Key + " / " + time.Error + ", expected " + due + " / " + key);
        count++;
    }
    static void Reject(string text)
    {
        var time = PaperTime.Read(text, Friday);
        if (time.Error == null) throw new Exception("Expected clarification: " + text + " => " + time.Key);
        count++;
    }
    static void Ordinary(string text)
    {
        var time = PaperTime.Read(text, Friday);
        if (time.Error != null || time.Key != null || time.Utc != 0) throw new Exception("Expected plain note: " + text);
        count++;
    }
    static int Main()
    {
        try { return Run(); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    static int Run()
    {
        foreach (string s in new[]{"10分钟", "过十分钟叫我", "设置一个10分钟的提醒", "过10分钟提醒我喝水", "拿快递，十分钟后再去", "１０分钟以后提醒我"})
            Expect(s, Friday.AddMinutes(10), "+10");
        foreach (string s in new[]{"半小时", "半个小时后", "半小时后出门", "提醒我：半小时", "再过30分钟出门"})
            Expect(s, Friday.AddMinutes(30), "+30");
        Expect("再过一刻钟",Friday.AddMinutes(15),"+15");
        Expect("过半个钟头叫我",Friday.AddMinutes(30),"+30");
        Expect("给三点建议，明天下午三点前来开会",Friday.Date.AddDays(1).AddHours(15),"明天@15:0");
        foreach (string s in new[]{"一个半小时后", "一小时半后", "1小时30分钟后提醒我", "一个小时零三十分钟后"})
            Expect(s, Friday.AddMinutes(90), "+90");
        Expect("7天后出门", Friday.AddDays(7), "+10080");
        Expect("1天2小时后提醒我",Friday.AddHours(26),"+1560");
        Expect("一天零两个小时三十分钟后",Friday.AddDays(1).AddMinutes(150),"+1590");
        Expect("两天又十五分钟后",Friday.AddDays(2).AddMinutes(15),"+2895");
        Expect("拿快递，明天下午3点提醒我", Friday.Date.AddDays(1).AddHours(15), "明天@15:0");
        Expect("帮我记得明早九点半开会", Friday.Date.AddDays(1).AddHours(9.5), "明天@9:30");
        Expect("明天提醒我下午三点取件", Friday.Date.AddDays(1).AddHours(15), "明天@15:0");
        Expect("今天晚上十二点关灯", Friday.Date.AddDays(1), "今天+1@0:0");
        Expect("等到后天早上8:05提醒我", Friday.Date.AddDays(2).AddHours(8).AddMinutes(5), "后天@8:5");
        Expect("带钥匙，９：０５", Friday.Date.AddDays(1).AddHours(9).AddMinutes(5), "@9:5");
        Expect("十点一刻", Friday.Date.AddDays(1).AddHours(10).AddMinutes(15), "@10:15");
        Expect("周日16:40帮我取件", Friday.Date.AddDays(2).AddHours(16).AddMinutes(40));
        Expect("这个星期五晚上八点", Friday.Date.AddHours(20));
        Expect("下周一上午九点", Friday.Date.AddDays(3).AddHours(9));
        Expect("下周一晚八点", Friday.Date.AddDays(3).AddHours(20));
        Expect("下下周日8点", Friday.Date.AddDays(16).AddHours(8));
        Expect("周五9点", Friday.Date.AddDays(7).AddHours(9));
        Expect("二〇二八年二月二十九日 下午三点", new DateTime(2028,2,29,15,0,0,DateTimeKind.Local));
        Expect("2027-01-03 08:05开工", new DateTime(2027,1,3,8,5,0,DateTimeKind.Local));
        Expect("取快递，12月1号9点", new DateTime(2026,12,1,9,0,0,DateTimeKind.Local));
        Expect("下个月15号下午3点",new DateTime(2026,12,15,15,0,0,DateTimeKind.Local));
        Expect("8号下午三点",new DateTime(2026,12,8,15,0,0,DateTimeKind.Local));
        Expect("31日9点",new DateTime(2026,12,31,9,0,0,DateTimeKind.Local));
        Expect("下下月一号上午八点",new DateTime(2027,1,1,8,0,0,DateTimeKind.Local));
        Expect("2月29日12:00", new DateTime(2028,2,29,12,0,0,DateTimeKind.Local));
        Expect("后年2月29号12点",new DateTime(2028,2,29,12,0,0,DateTimeKind.Local));
        Expect("每周五下午四点交周报",Friday.Date.AddHours(16),"repeat:32/960");
        Expect("每周五下午四点锻炼30分钟",Friday.Date.AddHours(16),"repeat:32/960");
        Expect("每周五下午四点开会，持续30分钟",Friday.Date.AddHours(16),"repeat:32/960");
        Expect("练琴半小时，每天下午四点",Friday.Date.AddHours(16),"repeat:127/960");
        Expect("提醒我，每个星期五9点出门",Friday.Date.AddDays(7).AddHours(9),"repeat:32/540");
        Expect("每周一上午九点",Friday.Date.AddDays(3).AddHours(9),"repeat:2/540");
        Expect("每日17:05查收",Friday.Date.AddHours(17).AddMinutes(5),"repeat:127/1025");
        Expect("每晚八点整理",Friday.Date.AddHours(20),"repeat:127/1200");
        Expect("工作安排：每周一至周五8点出门",Friday.Date.AddDays(3).AddHours(8),"repeat:62/480");
        Expect("每个工作日16点",Friday.Date.AddHours(16),"repeat:62/960");
        Expect("每周五晚上十二点",Friday.Date.AddDays(1),"repeat:64/0");
        var meeting=PaperTime.Read("每周五下午四点开会",Friday);
        var later=Friday.AddDays(24);
        if(PaperTime.NextRepeat(meeting.RepeatKey,later)!=Friday.Date.AddDays(28).AddHours(16).ToUniversalTime().Ticks ||
            PaperTime.NextRepeat(meeting.RepeatKey,Friday.Date.AddHours(16))!=Friday.Date.AddDays(7).AddHours(16).ToUniversalTime().Ticks)
            throw new Exception("Repeating notes must skip missed occurrences and advance past the exact current minute");
        foreach (string s in new[]{"今天9点去开会", "本周四15:00", "2026-01-01 07:30", "2027年2月29日早上九点",
            "2028年2月30日9:00", "13月1号上午9点", "明天25点", "今天晚上五点六十分", "10:090", "0分钟后出门",
            "8天后出门", "周末提醒我散步", "明天下午叫我", "晚点提醒我", "一会儿后拿快递", "每周一交稿",
            "每周五下午4点或5点交周报","每周五下午四点，半小时后提醒我","每周五下午四点之前提醒我","下个月开始每周五下午四点提醒我","不要每周五四点提醒我",
            "每周五下午四点开会，持续三周","每周五下午四点开会，两周一次","每周五下午四点开会，只提醒两次",
            "每半小时提醒我", "不用提醒我明天9点出门", "不要在三点提醒我", "下午三点左右提醒我", "明天9点或者10点",
            "10分钟或半小时后提醒我", "三到四点提醒我", "周一到周五下午五点", "明天或后天下午三点",
            "9:00 到 10:00", "10分钟前提醒我", "-10分钟后", "下午三点前提醒我", "五月四日", "明天下午3点和后天6点",
            "提前10分钟提醒我", "三五分钟后再去", "明年2月29号12点", "9:12:45 提醒我", "三点四刻提醒我",
            "到家时提醒我喂猫", "吃完饭叫我收衣服", "提醒我开会半小时", "明天拿快递", "下周下午三点", "明年下午三点",
            "每两个小时提醒我", "隔两小时提醒我", "国庆节上午九点", "明年的今天三点", "下周三和周四下午三点",
            "三点差一刻", "五点差十分", "这个月31日八点", "10分钟5分钟后提醒我", "朋友生日当天上午九点提醒我"})
            Reject(s);
        foreach (string s in new[]{"开会半小时", "跑步30分钟", "今天跑步用了三十分钟", "三点建议", "这个观点",
            "几点想法", "这次会议有三点要说", "第一时间", "有时间整理资料", "三点一线", "给你一点小心意", "我一点也不困",
            "每天一点也不着急","每天三点建议","每天一点点进步","我跑过半小时", "会议超过30分钟", "提醒事项列表", "咖啡豆和牛奶", "", null})
            Ordinary(s);
        if (PaperTime.Read("今天上午九点取件；明天下午三点也去",Friday).Key != null)
            throw new Exception("Ambiguous past and future instructions inherited a past deadline key");
        var longest = new string('九', 2048);
        var clock = Stopwatch.StartNew();
        for (int i=0;i<100;i++) PaperTime.Read(longest,Friday);
        if (clock.ElapsedMilliseconds > 1500) throw new Exception("Slow interactive parsing: " + clock.ElapsedMilliseconds);
        Console.WriteLine("PASS " + count + " Chinese note cases plus bounded long-input parsing (" + clock.ElapsedMilliseconds + "ms/100)");
        return 0;
    }
}
