using System.Net;

namespace AstarLiving.Client.Services;

/// <summary>
/// ข้อผิดพลาดที่ตั้งใจแสดงให้ผู้ใช้เห็นเป็นภาษาไทย
///
/// ข้อความมาจาก body.message ของ API เป็นหลัก เหมือนที่ frontend เดิมอ่าน
/// error.response.data.message มาโชว์ ถ้าไม่มีจึงค่อยใช้ข้อความสำรองตาม HTTP status
/// </summary>
public class ApiException : Exception
{
    public ApiException(string message, HttpStatusCode? status = null, int? bodyStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
        BodyStatusCode = bodyStatusCode;
    }

    /// <summary>HTTP status จริงที่ได้รับ (null เมื่อเชื่อมต่อไม่ถึงเลย)</summary>
    public HttpStatusCode? Status { get; }

    /// <summary>
    /// statusCode ที่อยู่ในเนื้อหา ซึ่งไม่จำเป็นต้องตรงกับ HTTP status
    /// ระบบเดิมคืน HTTP 200 พร้อม statusCode 404 ในกรณีไม่พบข้อมูล
    /// </summary>
    public int? BodyStatusCode { get; }

    /// <summary>true เมื่อปัญหาคือเครือข่าย ไม่ใช่ข้อมูลที่ผู้ใช้กรอก</summary>
    public bool IsConnectivity => Status is null;

    public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;

    /// <summary>เครื่องแม่บอกว่าโปรแกรมเวอร์ชันนี้เก่าเกินไป</summary>
    public bool IsUpgradeRequired => Status == HttpStatusCode.UpgradeRequired;

    public static ApiException Connectivity(string serverUrl, Exception? inner = null) =>
        new($"เชื่อมต่อเครื่องแม่ข่ายไม่ได้ ({serverUrl}) กรุณาตรวจสอบว่าเครื่องแม่ข่ายเปิดอยู่และอยู่ในเครือข่ายเดียวกัน",
            status: null, bodyStatusCode: null, inner);

    public static ApiException Timeout(string serverUrl, Exception? inner = null) =>
        new($"เครื่องแม่ข่ายไม่ตอบสนองภายในเวลาที่กำหนด ({serverUrl})",
            status: null, bodyStatusCode: null, inner);
}
