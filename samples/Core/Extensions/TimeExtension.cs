namespace Core;

public static class DateTimeExtension
{
    /// <summary>
    /// 获取13位时间戳
    /// </summary>
    /// <returns></returns>
    public static long ToTimeStamp13(this DateTime dateTime)
    {
        var ts = dateTime - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        
        return Convert.ToInt64(ts.TotalSeconds);
    }  
}