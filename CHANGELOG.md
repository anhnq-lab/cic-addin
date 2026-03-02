# Nhật ký cập nhật (Changelog) - CIC BIM Add-in

Tất cả các thay đổi đáng giá của dự án sẽ được ghi nhận tại file này.

## [1.0.5] - 2026-03-02

### Thay đổi & Cập nhật
- **Tường hoàn thiện (Wall Finish):** Viết lại toàn bộ — tạo tường theo biên dạng Room chuẩn, hỗ trợ offset biên dạng (co vào/nới ra).
- **Tường hoàn thiện:** Thêm tính năng gắn tên Room vào parameter tường/sàn đã tạo (mặc định: Comments).
- **Tường hoàn thiện:** Đơn giản hóa giao diện, bỏ phần trát cột/trát dầm phức tạp.

## [1.0.4] - 2026-03-01

### Thay đổi & Cập nhật
- **Smart QTO V4:** Cập nhật bộ giao diện Dark Mode nguyên khối sử dụng `ToolStyles` nhất quán.
- **Smart QTO V4:** Nâng cấp bảng xem trước dữ liệu (DataGrid) gom nhóm trực quan 2 tầng phân cấp (Tầng -> Hạng mục) dùng `CollectionViewSource`.
- **Smart QTO V4:** Khắc phục lỗi hiển thị ở danh sách cấu kiện: chữ nhạt bị chìm nghỉm vô hình khi lướt chuột qua (Sửa lỗi do nền trắng mặc định của Window WPF).
- **Lõi Add-in:** Khắc phục lỗi `DeferrableContent` cản trở khởi động Add-in do xung đột ghi đè `SystemColors`.

## [1.0.3] - 2026-03-01

### Thay đổi & Tính năng (Feature)
- Tool **Smart QTO V3**:
  - Giao diện UI được chia 2 cột: Cấu hình và Bảng Dữ Liệu Xem Trước (DataGrid Preview).
  - Tích hợp tính năng chạy tính toán khối lượng trực tiếp trên Giao Diện (Realtime Data Calculation) trước khi Xuất Excel.
  - Hỗ trợ lưu trữ tuỳ biến bằng hộp thoại (Save File Dialog) với tên File được đánh dấu theo giờ, thay vì lưu ngầm vào hệ thống.

## [1.0.2] - 2026-03-01

### Thêm mới (Added)
- Tool **Smart QTO V2**: 
  - Thêm tuỳ chọn Phân loại dữ liệu theo Tầng (Level/Reference Level).
  - Bổ sung nhóm hạng mục **Cơ Điện (MEP)**: Ống nước (Pipes), Phụ kiện (Fittings), Ống gió (Ducts), Thang Máng Cáp (Cable Trays).
  - Xuất bổ sung thông số cấu kiện "Kích thước / Dày" vào bảng BOQ để bóc tách tiết diện (ví dụ tường Dày 220mm, ống gió 400x200mm).

## [1.0.1] - 2026-03-01

### Thêm mới (Added)
- Tool **Bóc KL BOQ (Smart QTO)**: Công cụ cho phép kỹ sư bóc tách tự động KHỐI LƯỢNG THỰC TẾ đã trừ giao cắt của đối tượng BIM (Thể tích Bê tông, Diện tích Cốp pha/Xây trát, Chiều dài...).
- Tính năng xuất **Báo Cáo Excel (BOQ)** với định dạng chuyên nghiệp thông qua thư viện ClosedXML.

### Thay đổi (Changed)
- **Thiết kế lại toàn bộ Icon ứng dụng**: Chuyển sang phong cách Minimalist hiện đại (tăng vùng đệm padding, nền pastel, chữ sắc nét) giúp giao diện gọn gàng và tinh tế hơn trên Revit.

## [1.0.0] - Phát hành ban đầu

### Thêm mới (Added)
- Các công cụ quản lý và khai thác BIM (Gán tham số, Vẽ ống CAD, Bóc ván khuôn B3.2, Quản lý FM...).
- Đóng gói cài đặt Exe hỗ trợ cho Revit 2024 và Revit 2025.
