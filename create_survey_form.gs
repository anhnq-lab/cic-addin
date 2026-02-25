/**
 * ========================================================
 * CIC BIM Addin - Bảng Khảo Sát Nhân Viên Dựng Hình
 * ========================================================
 * 
 * HƯỚNG DẪN SỬ DỤNG:
 * 1. Truy cập https://script.google.com
 * 2. Tạo project mới (New Project)
 * 3. Copy toàn bộ nội dung file này vào editor
 * 4. Nhấn nút ▶ Run (chọn hàm createSurveyForm)
 * 5. Lần đầu chạy sẽ yêu cầu cấp quyền → Cho phép (Allow)
 * 6. Kiểm tra log (View > Logs) để lấy link Google Form
 */

function createSurveyForm() {
  const form = FormApp.create('📋 Khảo sát BIM Automation – CIC BIM Addin');
  form.setDescription(
    'Bảng khảo sát này nhằm thu thập thông tin từ đội ngũ BIM Modelers, phục vụ việc xây dựng CIC BIM Addin.\n\n' +
    '⏱ Thời gian hoàn thành: ~15-20 phút\n' +
    '🎯 Mục tiêu: Xác định mức độ ưu tiên các tool automation và thu thập thông tin kỹ thuật cần thiết.\n\n' +
    'Cảm ơn bạn đã dành thời gian!'
  );
  form.setConfirmationMessage('Cảm ơn bạn đã hoàn thành khảo sát! Kết quả sẽ được tổng hợp và phân tích để xây dựng CIC BIM Addin phù hợp nhất.');
  form.setAllowResponseEdits(true);
  form.setProgressBar(true);

  // ============================================================
  // PHẦN A: THÔNG TIN CÁ NHÂN
  // ============================================================
  form.addPageBreakItem().setTitle('PHẦN A: THÔNG TIN CÁ NHÂN');

  form.addTextItem()
    .setTitle('A1. Họ và tên')
    .setRequired(true);

  form.addListItem()
    .setTitle('A2. Bộ phận / Nhóm dự án')
    .setChoiceValues(['Kết cấu (KC)', 'Kiến trúc (KT)', 'Cơ điện (MEP)', 'Hạ tầng', 'Khác'])
    .setRequired(true);

  form.addCheckboxItem()
    .setTitle('A3. Chuyên môn chính (có thể chọn nhiều)')
    .setChoiceValues(['Kết cấu', 'Kiến trúc', 'Cơ điện', 'PCCC', 'Cấp thoát nước', 'HVAC', 'Điện', 'Hạ tầng'])
    .setRequired(true);

  form.addListItem()
    .setTitle('A4. Số năm kinh nghiệm sử dụng Revit')
    .setChoiceValues(['Dưới 1 năm', '1-2 năm', '2-5 năm', 'Trên 5 năm'])
    .setRequired(true);

  form.addCheckboxItem()
    .setTitle('A5. Phiên bản Revit đang sử dụng (có thể chọn nhiều)')
    .setChoiceValues(['Revit 2021', 'Revit 2022', 'Revit 2023', 'Revit 2024', 'Revit 2025']);

  form.addCheckboxItem()
    .setTitle('A6. Bạn có dùng add-in / plugin nào khác không? (có thể chọn nhiều)')
    .setChoiceValues(['pyRevit', 'Bimspeed', 'DiRoots', 'Dynamo BIM', 'CTC Tools', 'DS Cons', 'Không dùng add-in nào', 'Khác']);

  form.addTextItem()
    .setTitle('A6b. Nếu chọn "Khác" ở câu A6, vui lòng ghi rõ tên add-in');

  // ============================================================
  // PHẦN B: ĐÁNH GIÁ MỨC ĐỘ ƯU TIÊN CÁC Ý TƯỞNG
  // ============================================================

  // --- B1: Nhóm Dựng hình ---
  form.addPageBreakItem()
    .setTitle('PHẦN B1: Đánh giá ý tưởng – Nhóm DỰNG HÌNH')
    .setHelpText(
      'Hãy đánh giá mỗi ý tưởng automation theo:\n' +
      '• Tần suất bạn gặp công việc này\n' +
      '• Mức độ cần thiết phải có tool tự động (1 = Không cần, 5 = Rất cần)\n\n' +
      'Lưu ý: Nếu công việc không liên quan đến bạn, chọn "Không liên quan".'
    );

  var dungHinhItems = [
    ['B1.01 – Generate Void từ Link', 'Tạo khối Void từ cấu kiện file link để join cắt với file đang làm'],
    ['B1.02 – Quick Crop', 'Tạo Crop xoay theo chiều đối tượng, crop nhanh và chính xác'],
    ['B1.03 – QAQC Model', 'Thống kê và xử lý warning trong model Revit'],
    ['B1.04 – Auto Join', 'Tự động join đối tượng theo quy định ưu tiên (sàn > dầm > cột > tường)'],
    ['B1.05 – Beam Rebar', 'Model thép dầm tự động: nhập quy định số lượng, đường kính, neo thép → tự sinh mô hình'],
    ['B1.06 – Filter Rebar', 'Quản lý, lọc và cập nhật model Rebar khi thay đổi version hồ sơ'],
    ['B1.07 – Foundation Model', 'Tạo các loại móng tự động từ bản vẽ CAD'],
    ['B1.08 – Lean Concrete', 'Tự động tạo lớp bê tông lót cho cấu kiện móng'],
    ['B1.09 – Drawing Finish', 'Tạo pattern model / pattern drafting giống hatch trong hồ sơ'],
    ['B1.10 – Plaster (Lớp trát)', 'Tự động tạo lớp trát tường, cột, dầm, sàn trong room được chọn (áp dụng cả file link)'],
    ['B1.11 – Auto Curved Beam Rebar', 'Vẽ thép dầm cong tự động'],
    ['B1.12 – Auto Align Text', 'Align text từ CAD link vào parameter cấu kiện gần nhất (dầm, cột, móng, cọc)'],
    ['B1.13 – Auto Join Layers', 'Auto join các phần tách lớp: Tường - Trát - Sơn (tránh lỗi cửa ẩn sau lớp trát)'],
    ['B1.14 – Auto Room Separator', 'Tạo đường Room Separator theo biên dạng cột/vách kết cấu'],
    ['B1.15 – Auto Finishing Layer', 'Tạo lớp trát, sơn, ốp, sàn, trần tự động bao quanh room'],
    ['B1.16 – Auto Lintel', 'Tạo lanh tô cửa, cửa sổ tự động theo vị trí family được quét chọn'],
    ['B1.17 – Tạo giằng tường', 'Tạo giằng tường tự động theo cao độ tùy biến tại vị trí tường xây'],
    ['B1.18 – Tạo len đá chân cửa', 'Tạo len đá chân cửa tự động tại vị trí cửa đi được quét chọn'],
    ['B1.19 – Auto Replace Material', 'Đổi vật liệu hàng loạt cho các đối tượng Model in Place'],
    ['B1.20 – CIC Family Online', 'Thư viện family online chuẩn hóa CIC, load trực tiếp vào file (giống Bimspeed)'],
    ['B1.21 – Cắt va chạm KC-KT', 'Tự động cắt đối tượng va chạm giữa file link KC và KT (VD: tường KT cắt bởi dầm KC)'],
    ['B1.22 – Đặt family từ CAD block', 'Đặt family thiết bị tự động vào vị trí block/symbol trong link CAD'],
    ['B1.23 – Chia khúc ống', 'Chia ống ngầm thành khúc theo kích thước nhà sản xuất + đặt gối đỡ tự động'],
    ['B1.24 – Pipe từ CAD line type', 'Dựng pipe tự động từ line type CAD (VD: PTN DN150, DN100...) + nhập cao độ'],
    ['B1.25 – Duct từ đường tim CAD', 'Dựng duct tự động từ đường tim ống trong CAD link + nhập size, BOD'],
    ['B1.26 – Điền family vào model theo block CAD', 'Chọn block CAD → chọn family + level + cao độ → tool đặt family vào vị trí tim block'],
    ['B1.27 – Kết nối ống nhánh thoát nước', 'Kích ống nhánh + trọn ống trục đứng → tự động kết nối dạng tê + chếch'],
    ['B1.28 – Thay đổi độ dốc ống', 'Nhập độ dốc → pipe fitting tự động xoay theo góc tương ứng']
  ];

  dungHinhItems.forEach(function(item) {
    form.addMultipleChoiceItem()
      .setTitle(item[0] + ' — Tần suất')
      .setHelpText(item[1])
      .setChoiceValues(['Hàng ngày', 'Hàng tuần', 'Hàng tháng', 'Hiếm khi', 'Không liên quan']);

    form.addScaleItem()
      .setTitle(item[0] + ' — Mức độ cần thiết')
      .setBounds(1, 5)
      .setLabels('Không cần', 'Rất cần');
  });

  // --- B2: Nhóm Bản vẽ ---
  form.addPageBreakItem()
    .setTitle('PHẦN B2: Đánh giá ý tưởng – Nhóm BẢN VẼ & SHEET');

  var banVeItems = [
    ['B2.1 – Grid 2D↔3D', 'Chuyển đổi trục 2D-3D hàng loạt khi triển khai bản vẽ'],
    ['B2.2 – AutoDim', 'Tự động tạo Dimension cho cọc, tường, trục, cửa'],
    ['B2.3 – Sheet Duplicate', 'Duplicate Sheet + sửa tên tự động với tiền tố/hậu tố'],
    ['B2.4 – Rename Mark (Find & Replace)', 'Sửa tên/mark cấu kiện hàng loạt theo dạng Find & Replace (VD: D2 → D3)']
  ];

  banVeItems.forEach(function(item) {
    form.addMultipleChoiceItem()
      .setTitle(item[0] + ' — Tần suất')
      .setHelpText(item[1])
      .setChoiceValues(['Hàng ngày', 'Hàng tuần', 'Hàng tháng', 'Hiếm khi', 'Không liên quan']);

    form.addScaleItem()
      .setTitle(item[0] + ' — Mức độ cần thiết')
      .setBounds(1, 5)
      .setLabels('Không cần', 'Rất cần');
  });

  // --- B3: Nhóm Khối lượng ---
  form.addPageBreakItem()
    .setTitle('PHẦN B3: Đánh giá ý tưởng – Nhóm KHỐI LƯỢNG');

  var khoiLuongItems = [
    ['B3.1 – Schedule → Excel', 'Xuất schedule Revit trực tiếp sang file Excel (bỏ qua bước txt → Excel)'],
    ['B3.2 – Calculate Formwork', 'Thống kê ván khuôn tự động (tạo cấu kiện ván khuôn hoặc tính trực tiếp)'],
    ['B3.3 – Gán Room/Space → Location', 'Tự động gán tên Room/Space vào biến Location cho thiết bị MEP và đường ống']
  ];

  khoiLuongItems.forEach(function(item) {
    form.addMultipleChoiceItem()
      .setTitle(item[0] + ' — Tần suất')
      .setHelpText(item[1])
      .setChoiceValues(['Hàng ngày', 'Hàng tuần', 'Hàng tháng', 'Hiếm khi', 'Không liên quan']);

    form.addScaleItem()
      .setTitle(item[0] + ' — Mức độ cần thiết')
      .setBounds(1, 5)
      .setLabels('Không cần', 'Rất cần');
  });

  // --- B4: Nhóm Va chạm ---
  form.addPageBreakItem()
    .setTitle('PHẦN B4: Đánh giá ý tưởng – Nhóm KIỂM TRA & VA CHẠM');

  form.addMultipleChoiceItem()
    .setTitle('B4.1 – Tìm/Lọc ống trục đứng — Tần suất')
    .setHelpText('Tìm ống thẳng đứng (gió + nước), kiểm tra va chạm với lỗ mở kỹ thuật, kiểm tra chiều dài giới hạn đẩy nước bơm')
    .setChoiceValues(['Hàng ngày', 'Hàng tuần', 'Hàng tháng', 'Hiếm khi', 'Không liên quan']);

  form.addScaleItem()
    .setTitle('B4.1 – Tìm/Lọc ống trục đứng — Mức độ cần thiết')
    .setBounds(1, 5)
    .setLabels('Không cần', 'Rất cần');

  // --- TOP 5 ---
  form.addPageBreakItem()
    .setTitle('PHẦN B5: Xếp hạng TOP 5');

  form.addParagraphTextItem()
    .setTitle('B5. Chọn TOP 5 ý tưởng bạn muốn có NHẤT (ghi mã, VD: B1.04, B1.10, B2.2, B3.1, B1.21)')
    .setHelpText('Liệt kê 5 mã ý tưởng bạn cho là quan trọng và cấp bách nhất, cách nhau bởi dấu phẩy.')
    .setRequired(true);

  // ============================================================
  // PHẦN C: THÔNG TIN KỸ THUẬT
  // ============================================================
  form.addPageBreakItem()
    .setTitle('PHẦN C: THÔNG TIN KỸ THUẬT')
    .setHelpText('Phần này rất quan trọng để developer hiểu workflow thực tế của bạn. Hãy trả lời chi tiết nhất có thể.');

  // C1: Workflow CAD
  form.addSectionHeaderItem()
    .setTitle('C1. Workflow dựng hình từ CAD');

  form.addCheckboxItem()
    .setTitle('C1.1. Bạn thường nhận file thiết kế dạng nào?')
    .setChoiceValues(['DWG (AutoCAD)', 'DXF', 'PDF', 'Revit link (.rvt)', 'IFC', 'Khác']);

  form.addParagraphTextItem()
    .setTitle('C1.2. Khi link CAD vào Revit, bạn dùng cách nào để xác định vị trí cấu kiện?')
    .setHelpText('VD: Dùng tọa độ block, dùng layer, dùng line type, đo thủ công...');

  form.addParagraphTextItem()
    .setTitle('C1.3. Các block CAD thường dùng có quy ước đặt tên không? Cho ví dụ cụ thể')
    .setHelpText('VD: Block đèn = "LIGHT-01", block ổ cắm = "OUTLET-D"...');

  form.addMultipleChoiceItem()
    .setTitle('C1.4. Bạn có dùng layer mapping khi link CAD không?')
    .setChoiceValues(['Có', 'Không', 'Không biết layer mapping là gì']);

  // C2: Quy ước đặt tên
  form.addSectionHeaderItem()
    .setTitle('C2. Quy ước đặt tên & tham số');

  form.addParagraphTextItem()
    .setTitle('C2.1. Quy ước đặt tên cấu kiện (dầm, cột, móng) của bạn / dự án?')
    .setHelpText('VD: Dầm = D1-T2, Cột = C-01, Móng = M-B1...');

  form.addParagraphTextItem()
    .setTitle('C2.2. Bạn có dùng Shared Parameter riêng không? Liệt kê tên các parameter thường dùng')
    .setHelpText('VD: Mark, Comments, CIC_Location, CIC_Phase...');

  form.addTextItem()
    .setTitle('C2.3. Format mã tài sản / thiết bị trong dự án (nếu có)')
    .setHelpText('VD: TB-HVAC-001, FCU-T3-02...');

  form.addMultipleChoiceItem()
    .setTitle('C2.4. Bạn có dùng Assembly Code hay Keynote không?')
    .setChoiceValues(['Assembly Code', 'Keynote', 'Cả hai', 'Không dùng']);

  // C3: Join & Cut
  form.addSectionHeaderItem()
    .setTitle('C3. Quy trình Join & Cut');

  form.addParagraphTextItem()
    .setTitle('C3.1. Thứ tự ưu tiên join hiện tại của bạn?')
    .setHelpText('VD: Sàn > Dầm > Cột > Tường, hoặc theo quy định riêng dự án');

  form.addCheckboxItem()
    .setTitle('C3.2. Bạn thường join giữa những loại cấu kiện nào?')
    .setChoiceValues(['Sàn - Dầm', 'Dầm - Cột', 'Tường - Sàn', 'Tường - Dầm', 'Tường - Cột', 'Lớp trát - Tường', 'Móng - Cọc', 'Khác']);

  form.addMultipleChoiceItem()
    .setTitle('C3.3. Bạn có join cấu kiện cross-file (giữa các file link) không?')
    .setChoiceValues(['Có, thường xuyên', 'Có, nhưng ít khi', 'Không']);

  form.addListItem()
    .setTitle('C3.4. Số warning trung bình trong model của bạn?')
    .setChoiceValues(['Dưới 100', '100 - 500', '500 - 1000', 'Trên 1000', 'Không để ý']);

  // C4: Lớp hoàn thiện
  form.addSectionHeaderItem()
    .setTitle('C4. Lớp hoàn thiện (Finishing)');

  form.addCheckboxItem()
    .setTitle('C4.1. Các loại lớp hoàn thiện bạn thường dựng trong model?')
    .setChoiceValues(['Trát', 'Sơn', 'Ốp gạch / đá', 'Trần thạch cao', 'Sàn nâng', 'Len / phào chỉ', 'Không dựng lớp hoàn thiện', 'Khác']);

  form.addTextItem()
    .setTitle('C4.2. Chiều dày lớp trát tiêu chuẩn bạn hay dùng?')
    .setHelpText('VD: 15mm, 20mm, 25mm');

  form.addMultipleChoiceItem()
    .setTitle('C4.3. Bạn dùng Wall compound layer (nhiều lớp trong 1 wall) hay tách riêng lớp trát?')
    .setChoiceValues(['Compound layer (nhiều lớp trong 1 wall)', 'Tách riêng lớp trát thành wall/floor riêng', 'Tùy dự án', 'Không rõ']);

  form.addMultipleChoiceItem()
    .setTitle('C4.4. Bạn có tạo Room/Space cho tất cả phòng trong model không?')
    .setChoiceValues(['Có, đầy đủ tất cả phòng', 'Có, nhưng chỉ một số phòng', 'Không tạo Room/Space']);

  // C5: Thống kê & Bản vẽ
  form.addSectionHeaderItem()
    .setTitle('C5. Thống kê & Bản vẽ');

  form.addParagraphTextItem()
    .setTitle('C5.1. Mô tả format file Excel khối lượng bạn thường xuất')
    .setHelpText('VD: Theo mẫu dự toán công ty, theo BOQ chủ đầu tư, theo hạng mục...');

  form.addListItem()
    .setTitle('C5.2. Bạn dùng bao nhiêu Sheet trong 1 dự án trung bình?')
    .setChoiceValues(['Dưới 20', '20 - 50', '50 - 100', 'Trên 100']);

  form.addTextItem()
    .setTitle('C5.3. Quy ước đặt tên Sheet trong dự án?')
    .setHelpText('VD: KC-01, KT-T01-MB, MEP-PCCC-01...');

  form.addMultipleChoiceItem()
    .setTitle('C5.4. Bạn có dùng Revision tracking trong Revit không?')
    .setChoiceValues(['Có', 'Không', 'Không biết tính năng này']);

  // C6: MEP
  form.addPageBreakItem()
    .setTitle('PHẦN C6: MEP Specifics')
    .setHelpText('Phần này dành cho nhóm MEP. Nếu bạn không làm MEP, có thể bỏ qua.');

  form.addCheckboxItem()
    .setTitle('C6.1. Hệ thống MEP bạn chủ yếu dựng?')
    .setChoiceValues(['HVAC (Điều hòa không khí)', 'PCCC (Phòng cháy chữa cháy)', 'Cấp nước', 'Thoát nước', 'Điện động lực', 'Điện chiếu sáng', 'IT / Mạng / Camera', 'Không làm MEP']);

  form.addParagraphTextItem()
    .setTitle('C6.2. Liệt kê Pipe Type / Duct Type bạn thường dùng')
    .setHelpText('VD: PTN DN150, PTN DN100, Duct 400x200, Round Duct Ø300...');

  form.addParagraphTextItem()
    .setTitle('C6.3. Quy cách ống PCCC bạn thường dùng?')
    .setHelpText('VD: PTN DN150, DN100, DN80, DN65, DN50...');

  form.addTextItem()
    .setTitle('C6.4. Giá trị Pipe Slope (độ dốc ống) thường dùng cho hệ thoát nước?')
    .setHelpText('VD: 1%, 2%, 1/100...');

  form.addCheckboxItem()
    .setTitle('C6.5. Fitting ống thoát bạn hay dùng?')
    .setChoiceValues(['Tê 45°', 'Tê 90°', 'Chếch 45°', 'Co 90°', 'Co 45°', 'Nối giảm', 'Khác']);

  form.addTextItem()
    .setTitle('C6.6. Chiều dài khúc ống tiêu chuẩn nhà sản xuất thường dùng?')
    .setHelpText('VD: 6m, 4m, 3m...');

  // ============================================================
  // PHẦN D: ĐỀ XUẤT BỔ SUNG
  // ============================================================
  form.addPageBreakItem()
    .setTitle('PHẦN D: ĐỀ XUẤT BỔ SUNG');

  form.addParagraphTextItem()
    .setTitle('D1. Công tác nào bạn mất nhiều thời gian nhất hiện tại? (Liệt kê Top 3)')
    .setHelpText('Mô tả cụ thể 3 công việc lặp đi lặp lại tốn thời gian nhất')
    .setRequired(true);

  form.addParagraphTextItem()
    .setTitle('D2. Bạn có ý tưởng automation nào khác chưa có trong danh sách trên không?')
    .setHelpText('Mô tả ý tưởng, công việc liên quan, và lợi ích mang lại');

  form.addCheckboxItem()
    .setTitle('D3. Bạn muốn tool chạy dưới dạng nào?')
    .setChoiceValues(['Button trên Ribbon (tab riêng CIC)', 'Context menu (chuột phải)', 'Shortcut key (phím tắt)', 'Dialog / Form nhập liệu', 'Tự động chạy khi mở file']);

  form.addMultipleChoiceItem()
    .setTitle('D4. Bạn có sẵn sàng test thử tool và phản hồi không?')
    .setChoiceValues(['Có, sẵn sàng', 'Có, nhưng cần sắp xếp thời gian', 'Không'])
    .setRequired(true);

  form.addListItem()
    .setTitle('D5. Thời gian bạn có thể dành cho việc test tool?')
    .setChoiceValues(['30 phút / tuần', '1 giờ / tuần', '2 giờ / tuần', 'Linh hoạt, tùy theo tool']);

  form.addParagraphTextItem()
    .setTitle('D6. Góp ý thêm (nếu có)')
    .setHelpText('Bất kỳ ý kiến, góp ý, hoặc mong muốn nào khác');

  // ============================================================
  // LOG RESULT
  // ============================================================
  Logger.log('✅ Google Form đã được tạo thành công!');
  Logger.log('📝 Tên form: ' + form.getTitle());
  Logger.log('🔗 Link chỉnh sửa: ' + form.getEditUrl());
  Logger.log('🌐 Link gửi cho nhân viên: ' + form.getPublishedUrl());
  Logger.log('📊 Link xem kết quả: ' + form.getSummaryUrl());
}
