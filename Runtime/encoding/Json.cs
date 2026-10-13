namespace Due
{
using UnityEngine;

// 基于 UnityEngine.JsonUtility 的 JSON 编解码器。
// 注意：JsonUtility 仅支持标记了 [System.Serializable] 的类/结构体，
// 且不支持字典、匿名对象、顶层数组等；如需更灵活的 JSON 支持，
// 可实现 IEncoding 接口并注入 Newtonsoft.Json 等第三方实现。
public class Json : IEncoding
{
    public Json()
    {
    }

    // 编码
    public byte[] Encode(object data)
    {
        return System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
    }

    // 解码
    public Data Decode(byte[] buff)
    {
        return new Data(buff);
    }
}

public class Data : IData
{
    private readonly byte[] buff;

    public Data(byte[] buff)
    {
        this.buff = buff;
    }

    // 获取对象数据
    public T GetObject<T>()
    {
        return JsonUtility.FromJson<T>(this.GetString());
    }

    // 获取字符串数据
    public string GetString()
    {
        return System.Text.Encoding.UTF8.GetString(this.buff);
    }

    // 获取字节数据
    public byte[] GetBytes()
    {
        return this.buff;
    }
}
}
