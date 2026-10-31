using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

/// <summary>
/// รหัสผ่านของผู้ใช้ทุกคนในฐานข้อมูลเดิมถูก hash ด้วยไลบรารี bcrypt ของ Node (รูปแบบ $2b$ cost 12)
/// ถ้า BCrypt.Net-Next ตรวจ hash เหล่านี้ไม่ผ่าน จะไม่มีใครล็อกอินเข้าระบบใหม่ได้เลย
/// เทสต์ชุดนี้จึงตรึงความเข้ากันได้ระหว่างสองไลบรารีไว้
/// </summary>
public class BcryptCompatibilityTests
{
    /// <summary>hash ที่สร้างจาก bcrypt ของ Node ในโปรเจคเดิมโดยตรง (cost 12)</summary>
    private const string NodeBcrypt2BHash = "$2b$12$KbfH6SI98aju/8J0IoLpXOUideaD6661hUVo535zBpEJuqIAuM4Vu";

    private const string NodeBcryptPassword = "AstarLiving#2568";

    /// <summary>
    /// hash รูปแบบ $2a$ ที่สร้างจาก bcrypt ของ Node เช่นกัน
    /// สำคัญเพราะ BCrypt.Net-Next สร้าง hash ใหม่เป็นรูปแบบ $2a$ ระบบทั้งสองฝั่งจึงต้องอ่านของกันและกันได้
    /// </summary>
    private const string Node2AHash = "$2a$10$CjmX/dLVEYsTZy4OxUaSUeMrzJg5yg.r1VhiyC4t9jP6tSaRwe.re";

    [Fact]
    public void Verifies_hash_produced_by_node_bcrypt()
    {
        BCrypt.Net.BCrypt.Verify(NodeBcryptPassword, NodeBcrypt2BHash).Should().BeTrue(
            "ผู้ใช้ทุกคนในฐานข้อมูลเดิมมี hash รูปแบบนี้");
    }

    [Fact]
    public void Rejects_wrong_password_against_node_hash() =>
        BCrypt.Net.BCrypt.Verify("ผิดแน่นอน", NodeBcrypt2BHash).Should().BeFalse();

    [Fact]
    public void Verifies_2a_format_as_well() =>
        BCrypt.Net.BCrypt.Verify(NodeBcryptPassword, Node2AHash).Should().BeTrue();

    /// <summary>ระบบเดิมใช้ cost 12 — ระบบใหม่ต้องใช้ค่าเดียวกันเพื่อให้ hash ที่สร้างใหม่เทียบเคียงกันได้</summary>
    [Fact]
    public void New_hashes_use_work_factor_twelve()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("ทดสอบรหัสผ่าน", 12);

        hash.Should().MatchRegex(@"^\$2[aby]\$12\$");
        BCrypt.Net.BCrypt.Verify("ทดสอบรหัสผ่าน", hash).Should().BeTrue();
    }
}
