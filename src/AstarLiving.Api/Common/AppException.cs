namespace AstarLiving.Api.Common;

/// <summary>
/// ข้อผิดพลาดที่ตั้งใจโยนออกไปให้ผู้ใช้เห็น พร้อมข้อความภาษาไทยของระบบเดิม
/// ตัวกรอง NestStyleExceptionFilter จะแปลงเป็น body รูปแบบเดียวกับ NestJS
/// </summary>
public class AppException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static AppException BadRequest(string message) => new(400, message);

    public static AppException Unauthorized(string message) => new(401, message);

    public static AppException Forbidden(string message) => new(403, message);

    /// <summary>
    /// ใช้เมื่อระบบเดิมโยน NotFoundException จริง ๆ (ได้ HTTP 404)
    /// ต่างจากกรณี "ไม่พบข้อมูล" ที่ระบบเดิมคืน HTTP 200 พร้อม statusCode 404 ใน body
    /// ซึ่งกรณีนั้นให้ใช้ ApiResponseFactory.NotFound แทน
    /// </summary>
    public static AppException NotFound(string message) => new(404, message);

    public static AppException Conflict(string message) => new(409, message);

    public static AppException Internal(string message) => new(500, message);

    /// <summary>
    /// ระบบยังให้บริการส่วนนี้ไม่ได้เพราะสภาพแวดล้อมไม่พร้อม เช่น หาเครื่องมือไม่เจอ
    /// ต่างจาก Internal ตรงที่ผู้ดูแลแก้เองได้ ข้อความจึงต้องบอกวิธีแก้ด้วย
    /// </summary>
    public static AppException ServiceUnavailable(string message) => new(503, message);
}
