/**
 * ========================================================
 * CIC BIM Addin — Thu thập Thông tin Kỹ thuật 5 Tools
 * ========================================================
 *
 * HƯỚNG DẪN SỬ DỤNG:
 * 1. Truy cập https://script.google.com
 * 2. Tạo project mới (New Project)
 * 3. Copy toàn bộ nội dung file này vào editor
 * 4. Nhấn nút ▶ Run (chọn hàm createTechnicalForm)
 * 5. Lần đầu chạy sẽ yêu cầu cấp quyền → Cho phép (Allow)
 * 6. Kiểm tra log (View > Logs) để lấy link Google Form
 */

function createTechnicalForm() {
  const form = FormApp.create('🔧 Thu thập Thông tin Kỹ thuật — CIC BIM Addin (5 Tools)');
  form.setDescription(
    'Tài liệu này thu thập thông tin kỹ thuật cần thiết để phát triển 5 tools automation ưu tiên cho CIC BIM Addin.\n\n' +
    '📋 5 Tools ưu tiên:\n' +
    '  1. B3.2 — Calculate Formwork: Tự động tính diện tích ván khuôn cho dầm, cột, tường, sàn, móng\n' +
    '  2. B1.10 — Plaster: Tự động tạo lớp trát tường, cột, dầm, sàn trong Room được chọn\n' +
    '  3. Đặt thiết bị theo Block CAD: Đọc block CAD (kể cả block động) → đặt Family Revit tương ứng\n' +
    '  4. Ống gió từ CAD: Dựng Duct tự động từ line trong bản vẽ CAD\n' +
    '  5. B1.28 — Thay đổi độ dốc ống: Nhập độ dốc → ống và fitting tự xoay theo\n\n' +
    '⏱ Thời gian hoàn thành: ~20-30 phút\n' +
    '💡 Bạn chỉ cần trả lời phần tool LIÊN QUAN đến công việc của bạn. Phần không liên quan có thể bỏ qua.\n\n' +
    'Cảm ơn bạn đã dành thời gian!'
  );
  form.setConfirmationMessage(
    'Cảm ơn bạn đã hoàn thành khảo sát!\n' +
    'Kết quả sẽ được tổng hợp để phát triển CIC BIM Addin.\n\n' +
    '⚠️ Đừng quên gửi kèm file mẫu (.rvt, .dwg, .xlsx) qua email hoặc thư mục chung!'
  );
  form.setAllowResponseEdits(true);
  form.setProgressBar(true);

  // ============================================================
  // PHẦN 0: THÔNG TIN CÁ NHÂN
  // ============================================================
  form.addPageBreakItem()
    .setTitle('THÔNG TIN NGƯỜI ĐIỀN')
    .setHelpText('Thông tin cơ bản để chúng tôi biết ai đang trả lời.');

  form.addTextItem()
    .setTitle('Họ và tên')
    .setRequired(true);

  form.addListItem()
    .setTitle('Bộ phận / Nhóm')
    .setChoiceValues(['Kết cấu (KC)', 'Kiến trúc (KT)', 'Cơ điện (MEP)', 'Hạ tầng', 'Khác'])
    .setRequired(true);

  form.addCheckboxItem()
    .setTitle('Phiên bản Revit đang sử dụng (có thể chọn nhiều)')
    .setChoiceValues(['Revit 2022', 'Revit 2023', 'Revit 2024', 'Revit 2025'])
    .setRequired(true);

  form.addCheckboxItem()
    .setTitle('Bạn liên quan đến tool nào? (chọn để chỉ trả lời phần liên quan)')
    .setChoiceValues([
      'Tool 1: Calculate Formwork (Ván khuôn) — KC',
      'Tool 2: Plaster (Lớp trát) — KT',
      'Tool 3: Đặt thiết bị theo Block CAD — MEP',
      'Tool 4: Ống gió từ line CAD — MEP',
      'Tool 5: Thay đổi độ dốc ống — MEP'
    ])
    .setRequired(true);

  // ============================================================
  // TOOL 1: CALCULATE FORMWORK
  // ============================================================
  form.addPageBreakItem()
    .setTitle('🔧 TOOL 1: B3.2 — CALCULATE FORMWORK (THỐNG KÊ VÁN KHUÔN)')
    .setHelpText(
      '📖 MÔ TẢ TÍNH NĂNG:\n' +
      'Tool này giúp tự động tính toán diện tích ván khuôn cho các cấu kiện kết cấu (dầm, cột, tường, sàn, móng) trong model Revit.\n\n' +
      '🔴 Hiện tại (thủ công):\n' +
      '• Mở từng cấu kiện → đo thủ công từng mặt cần ván khuôn\n' +
      '• Tính tay: trừ phần giao nhau dầm-cột, dầm-sàn\n' +
      '• Nhập kết quả vào Excel → mất nhiều giờ, dễ sai sót\n\n' +
      '🟢 Sau khi có tool (tự động):\n' +
      '• Chọn cấu kiện hoặc chạy toàn bộ model → tool tự quét geometry\n' +
      '• Tự tính diện tích các mặt cần ván khuôn, tự trừ phần giao nhau\n' +
      '• Xuất kết quả ra bảng thống kê hoặc Excel → chỉ mất vài phút\n\n' +
      '👥 Nhóm liên quan: KC (Kết cấu)\n' +
      '⚠️ Nếu bạn KHÔNG làm KC, có thể bỏ qua phần này.'
    );

  form.addMultipleChoiceItem()
    .setTitle('⭐ Mô tả tính năng ở trên có ĐÚNG với nhu cầu thực tế của bạn không?')
    .setChoiceValues([
      'Đúng — mô tả khớp với công việc tôi đang làm',
      'Đúng một phần — cần bổ sung/chỉnh sửa (ghi ở câu tiếp)',
      'Không đúng — tôi hiểu tool này khác (ghi ở câu tiếp)',
      'Không liên quan đến công việc của tôi'
    ]);

  form.addParagraphTextItem()
    .setTitle('↳ Nếu mô tả chưa đúng, bạn mong muốn tool hoạt động như thế nào?')
    .setHelpText('Mô tả chi tiết workflow bạn mong muốn. VD: "Tôi cần tool tính ván khuôn nhưng chỉ cho dầm và cột, không cần cho tường..."');

  // A. Phạm vi cấu kiện
  form.addSectionHeaderItem()
    .setTitle('A. Phạm vi cấu kiện');

  form.addCheckboxItem()
    .setTitle('1.1. Loại cấu kiện nào cần tính ván khuôn?')
    .setChoiceValues([
      'Dầm (Beam)',
      'Cột (Column)',
      'Tường (Wall)',
      'Sàn (Floor/Slab)',
      'Móng (Foundation)',
      'Cầu thang (Stair)',
      'Khác'
    ]);

  form.addTextItem()
    .setTitle('1.1b. Nếu chọn "Khác" → ghi rõ loại cấu kiện');

  // B. Quy tắc tính diện tích
  form.addSectionHeaderItem()
    .setTitle('B. Quy tắc tính diện tích ván khuôn');

  form.addCheckboxItem()
    .setTitle('1.2. Với DẦM — tính diện tích ván khuôn gồm những mặt nào?')
    .setChoiceValues([
      '2 mặt bên + đáy dầm',
      'Có trừ phần giao dầm-cột',
      'Có trừ phần dầm tiếp xúc với sàn',
      'Quy tắc khác (ghi ở câu tiếp)'
    ]);

  form.addParagraphTextItem()
    .setTitle('1.2b. Quy tắc tính ván khuôn DẦM chi tiết hơn (nếu cần)')
    .setHelpText('Mô tả cụ thể cách tính, VD: dầm console tính khác, dầm cong tính khác...');

  form.addCheckboxItem()
    .setTitle('1.3. Với CỘT — tính diện tích ván khuôn gồm những mặt nào?')
    .setChoiceValues([
      '4 mặt (cột vuông/chữ nhật)',
      'Chu vi × chiều cao (cột tròn)',
      'Có trừ phần giao cột-dầm',
      'Có trừ phần giao cột-sàn',
      'Quy tắc khác (ghi ở câu tiếp)'
    ]);

  form.addParagraphTextItem()
    .setTitle('1.3b. Quy tắc tính ván khuôn CỘT chi tiết hơn (nếu cần)');

  form.addParagraphTextItem()
    .setTitle('1.4. Với TƯỜNG — tính diện tích ván khuôn thế nào?')
    .setHelpText('VD: 2 mặt × cao × dài? Có trừ lỗ cửa? Có trừ phần giao sàn?');

  form.addParagraphTextItem()
    .setTitle('1.5. Với MÓNG — tính diện tích ván khuôn thế nào?')
    .setHelpText('VD: Móng đơn = 4 mặt bên? Móng băng = 2 mặt dài + 2 mặt đầu? Có trừ mặt tiếp đất?');

  form.addParagraphTextItem()
    .setTitle('1.6. Cấu kiện ĐẶC BIỆT: Dầm console, dầm cong, lanh tô — có tính không? Quy tắc riêng?');

  // C. Kết quả đầu ra
  form.addSectionHeaderItem()
    .setTitle('C. Kết quả đầu ra');

  form.addMultipleChoiceItem()
    .setTitle('1.7. Đơn vị kết quả ván khuôn:')
    .setChoiceValues(['m²', 'Đơn vị khác (ghi rõ ở câu sau)']);

  form.addCheckboxItem()
    .setTitle('1.8. Cần nhóm kết quả theo tiêu chí nào?')
    .setChoiceValues([
      'Theo Level (tầng)',
      'Theo loại cấu kiện',
      'Theo Zone / Area',
      'Theo Mark cấu kiện',
      'Khác'
    ]);

  form.addParagraphTextItem()
    .setTitle('1.9. Mô tả format bảng kết quả mong muốn')
    .setHelpText('VD: Cột A = Tên cấu kiện, Cột B = Mark, Cột C = Level, Cột D = Diện tích VK (m²)...\nHoặc mô tả file Excel mẫu bạn sẽ gửi kèm.');

  // D. File mẫu
  form.addSectionHeaderItem()
    .setTitle('D. File mẫu cần cung cấp')
    .setHelpText('Upload file lên Google Drive → Bật chia sẻ "Anyone with the link" → Dán link vào ô bên dưới.');

  form.addCheckboxItem()
    .setTitle('1.10. Bạn có thể cung cấp file nào?')
    .setChoiceValues([
      'File .rvt mẫu có cấu kiện KC thực tế',
      'File Excel mẫu format kết quả ván khuôn',
      'Screenshot bản vẽ thể hiện cách đo ván khuôn thủ công',
      'Chưa có, cần thời gian chuẩn bị'
    ]);

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File .rvt mẫu KC (Tool 1)')
    .setHelpText('Dán link Google Drive ở đây. VD: https://drive.google.com/file/d/...');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File Excel mẫu kết quả ván khuôn (Tool 1)')
    .setHelpText('Bảng Excel mẫu thể hiện format kết quả mong muốn');

  // ============================================================
  // TOOL 2: PLASTER
  // ============================================================
  form.addPageBreakItem()
    .setTitle('🧱 TOOL 2: B1.10 — PLASTER (TẠO LỚP TRÁT TỰ ĐỘNG)')
    .setHelpText(
      '📖 MÔ TẢ TÍNH NĂNG:\n' +
      'Tool này giúp tự động tạo lớp trát (vữa xi măng) bao quanh tường, cột, dầm, sàn bên trong các Room được chọn. Hỗ trợ cả cấu kiện từ file link KC.\n\n' +
      '🔴 Hiện tại (thủ công):\n' +
      '• Vẽ tay từng tường trát (Wall mỏng) song song với tường chính\n' +
      '• Canh chỉnh thủ công chiều dày, chiều cao, vị trí\n' +
      '• Cắt xén quanh cửa, cắt giao với dầm/cột → cực kỳ tốn thời gian\n' +
      '• Dễ bỏ sót, đặc biệt khi dự án có hàng trăm phòng\n\n' +
      '🟢 Sau khi có tool (tự động):\n' +
      '• Chọn Room → tool tự tạo lớp trát bao quanh tường, cột, dầm, sàn\n' +
      '• Tự cắt quanh cửa, tự xử lý giao cắt với cấu kiện KC link\n' +
      '• Áp dụng đúng chiều dày, vật liệu theo từng loại cấu kiện\n' +
      '• Xử lý hàng trăm phòng chỉ trong vài phút\n\n' +
      '👥 Nhóm liên quan: KT (Kiến trúc)\n' +
      '⚠️ Nếu bạn KHÔNG làm KT, có thể bỏ qua phần này.'
    );

  form.addMultipleChoiceItem()
    .setTitle('⭐ Mô tả tính năng ở trên có ĐÚNG với nhu cầu thực tế của bạn không?')
    .setChoiceValues([
      'Đúng — mô tả khớp với công việc tôi đang làm',
      'Đúng một phần — cần bổ sung/chỉnh sửa (ghi ở câu tiếp)',
      'Không đúng — tôi hiểu tool này khác (ghi ở câu tiếp)',
      'Không liên quan đến công việc của tôi'
    ]);

  form.addParagraphTextItem()
    .setTitle('↳ Nếu mô tả chưa đúng, bạn mong muốn tool hoạt động như thế nào?')
    .setHelpText('Mô tả chi tiết. VD: "Tool cần trát cả mặt ngoài tường, không chỉ mặt trong Room..."');

  // A. Chiều dày lớp trát
  form.addSectionHeaderItem()
    .setTitle('A. Chiều dày lớp trát');

  form.addTextItem()
    .setTitle('2.1. Chiều dày lớp trát TƯỜNG TRONG (mm)')
    .setHelpText('VD: 15');

  form.addTextItem()
    .setTitle('2.2. Chiều dày lớp trát TƯỜNG NGOÀI (mm)')
    .setHelpText('VD: 20');

  form.addTextItem()
    .setTitle('2.3. Chiều dày lớp trát CỘT (mm)')
    .setHelpText('VD: 15');

  form.addTextItem()
    .setTitle('2.4. Chiều dày lớp trát DẦM — mặt dưới (mm)')
    .setHelpText('VD: 15');

  form.addTextItem()
    .setTitle('2.5. Chiều dày lớp trát DẦM — mặt bên (mm)')
    .setHelpText('VD: 15');

  form.addTextItem()
    .setTitle('2.6. Chiều dày lớp trát SÀN — mặt dưới/trần (mm)')
    .setHelpText('VD: 15');

  form.addMultipleChoiceItem()
    .setTitle('2.7. Chiều dày có thay đổi theo dự án hay dùng 1 giá trị cố định?')
    .setChoiceValues([
      'Cố định — luôn dùng 1 giá trị',
      'Thay đổi — tùy dự án',
      'Cả hai — muốn tool cho nhập tùy chọn khi chạy'
    ]);

  // B. Wall Type & Material
  form.addSectionHeaderItem()
    .setTitle('B. Wall Type & Material');

  form.addTextItem()
    .setTitle('2.8. Wall Type / Floor Type dùng cho lớp trát tên gì?')
    .setHelpText('VD: Wall_Plaster_15mm — nếu chưa có, tool sẽ tự tạo');

  form.addTextItem()
    .setTitle('2.9. Material (vật liệu) lớp trát dùng tên gì trong model?')
    .setHelpText('VD: Vữa trát xi măng, Plaster - Cement Mortar...');

  // C. Phạm vi áp dụng
  form.addSectionHeaderItem()
    .setTitle('C. Phạm vi áp dụng');

  form.addMultipleChoiceItem()
    .setTitle('2.10. Phạm vi tạo lớp trát:')
    .setChoiceValues([
      'Theo Room được chọn',
      'Toàn bộ Room trong 1 Level',
      'Toàn bộ Room trong model',
      'Tùy chọn khi chạy tool'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('2.11. Trát mặt nào của tường?')
    .setChoiceValues([
      'Chỉ mặt trong Room',
      'Cả 2 mặt tường',
      'Tùy chọn khi chạy tool'
    ]);

  form.addCheckboxItem()
    .setTitle('2.12. Xử lý cửa (Door / Window):')
    .setChoiceValues([
      'Lớp trát tự cắt quanh cửa',
      'Lớp trát phủ qua (user tự cắt)',
      'Tạo thêm lớp trát bao quanh khuôn cửa (laibô cửa)'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('2.13. Có trát quanh cấu kiện từ file KC link (cột KC, dầm KC) không?')
    .setChoiceValues([
      'Có — tool cần đọc geometry từ file link',
      'Không — chỉ trát tường/sàn trong file hiện tại'
    ]);

  // D. Yêu cầu bổ sung
  form.addSectionHeaderItem()
    .setTitle('D. Yêu cầu bổ sung');

  form.addMultipleChoiceItem()
    .setTitle('2.14. Có cần tạo lớp len đá chân tường (skirting) kèm theo không?')
    .setChoiceValues(['Có', 'Không', 'Tùy dự án']);

  form.addMultipleChoiceItem()
    .setTitle('2.15. Có cần tạo lớp sơn bên ngoài lớp trát không?')
    .setChoiceValues(['Có', 'Không', 'Tùy dự án']);

  form.addCheckboxItem()
    .setTitle('2.16. File mẫu bạn có thể cung cấp:')
    .setChoiceValues([
      'File .rvt mẫu có Room đã đặt sẵn',
      'Screenshot / PDF bản vẽ thể hiện lớp trát mong muốn',
      'Template dự án (.rte) có sẵn Wall Type lớp trát',
      'Chưa có, cần chuẩn bị'
    ]);

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File .rvt mẫu có Room (Tool 2)')
    .setHelpText('Dán link Google Drive ở đây. VD: https://drive.google.com/file/d/...');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Screenshot/PDF bản vẽ lớp trát (Tool 2)')
    .setHelpText('Screenshot hoặc bản vẽ thể hiện kết quả mong muốn');

  // ============================================================
  // TOOL 3: ĐẶT THIẾT BỊ THEO BLOCK CAD
  // ============================================================
  form.addPageBreakItem()
    .setTitle('📦 TOOL 3: ĐẶT THIẾT BỊ THEO BLOCK CAD')
    .setHelpText(
      '📖 MÔ TẢ TÍNH NĂNG:\n' +
      'Tool này giúp tự động đặt thiết bị (Family) trong Revit dựa trên vị trí các Block trong file CAD link. Hỗ trợ cả Block tĩnh và Block động (Dynamic Block). Mỗi Block CAD được mapping sang 1 Family Revit tương ứng.\n\n' +
      '🔴 Hiện tại (thủ công):\n' +
      '• Nhìn bản vẽ CAD link → xác định vị trí từng thiết bị từ block/symbol\n' +
      '• Đặt tay từng Family Revit vào đúng vị trí, đúng góc xoay\n' +
      '• Block động (Dynamic Block) có nhiều trạng thái → khó xác định size/type\n' +
      '• Dự án lớn có hàng trăm thiết bị → mất rất nhiều giờ, dễ sót\n\n' +
      '🟢 Sau khi có tool (tự động):\n' +
      '• Chọn layer/block name trong CAD → tool tự nhận diện loại thiết bị\n' +
      '• Tự đặt Family Revit đúng vị trí, đúng góc xoay theo block CAD\n' +
      '• Đọc được thuộc tính Block động → chọn đúng Type/Size Family\n' +
      '• Xử lý hàng trăm thiết bị chỉ trong vài phút\n\n' +
      '👥 Nhóm liên quan: MEP (Cơ điện / PCCC / HVAC)\n' +
      '⚠️ Nếu bạn KHÔNG làm MEP, có thể bỏ qua phần này.'
    );

  form.addMultipleChoiceItem()
    .setTitle('⭐ Mô tả tính năng ở trên có ĐÚNG với nhu cầu thực tế của bạn không?')
    .setChoiceValues([
      'Đúng — mô tả khớp với công việc tôi đang làm',
      'Đúng một phần — cần bổ sung/chỉnh sửa (ghi ở câu tiếp)',
      'Không đúng — tôi hiểu tool này khác (ghi ở câu tiếp)',
      'Không liên quan đến công việc của tôi'
    ]);

  form.addParagraphTextItem()
    .setTitle('↳ Nếu mô tả chưa đúng, bạn mong muốn tool hoạt động như thế nào?')
    .setHelpText('Mô tả chi tiết workflow bạn mong muốn.');

  // A. Loại thiết bị & Block CAD
  form.addSectionHeaderItem()
    .setTitle('A. Loại thiết bị & Block CAD');

  form.addCheckboxItem()
    .setTitle('3.1. Loại thiết bị nào cần đặt từ Block CAD?')
    .setChoiceValues([
      'Đầu phun sprinkler (PCCC)',
      'Đầu báo khói / báo nhiệt',
      'Đèn chiếu sáng (Lighting Fixture)',
      'Ổ cắm điện / công tắc',
      'Miệng gió (Diffuser / Grille)',
      'Quạt (Fan)',
      'Thiết bị vệ sinh (Plumbing Fixture)',
      'Thiết bị HVAC (FCU, AHU, VRF...)',
      'Khác'
    ]);

  form.addTextItem()
    .setTitle('3.1b. Nếu chọn "Khác" → ghi rõ loại thiết bị');

  // B. Bảng mapping Block → Family
  form.addSectionHeaderItem()
    .setTitle('B. Bảng mapping Block CAD → Family Revit');

  form.addParagraphTextItem()
    .setTitle('3.2. Liệt kê TẤT CẢ Block CAD và Family Revit tương ứng')
    .setHelpText(
      'Điền theo format (mỗi dòng 1 mapping):\n' +
      'Block CAD → Family Revit | Type\n\n' +
      'VD:\n' +
      'SPK_UP → Sprinkler - Upright | DN15\n' +
      'SPK_PEND → Sprinkler - Pendent | DN15\n' +
      'SMOKE_DET → Smoke Detector | Photoelectric\n' +
      'LIGHT_600x600 → Panel Light | 600x600\n' +
      '...'
    );

  // C. Block động (Dynamic Block)
  form.addSectionHeaderItem()
    .setTitle('C. Block động (Dynamic Block)');

  form.addMultipleChoiceItem()
    .setTitle('3.3. Bản vẽ CAD có sử dụng Block động không?')
    .setChoiceValues([
      'Có — Block động với nhiều trạng thái (Visibility States)',
      'Có — Block động với thay đổi kích thước (Stretch/Scale)',
      'Không — chỉ có Block tĩnh thông thường',
      'Không chắc'
    ]);

  form.addParagraphTextItem()
    .setTitle('3.4. Thuộc tính Block động nào cần đọc để chọn đúng Family Type?')
    .setHelpText('VD: Visibility State "UP" → Sprinkler Upright, State "PEND" → Sprinkler Pendent\nHoặc: Attribute "SIZE" = 600x600 → Panel Light 600x600');

  // D. Vị trí & Góc xoay
  form.addSectionHeaderItem()
    .setTitle('D. Vị trí & Góc xoay');

  form.addMultipleChoiceItem()
    .setTitle('3.5. Cao độ (Elevation) thiết bị xác định thế nào?')
    .setChoiceValues([
      'Nhập 1 cao độ chung cho tất cả thiết bị cùng loại',
      'Mỗi loại thiết bị có cao độ riêng',
      'Lấy từ attribute/text trong CAD',
      'Đặt sát trần (tự tính theo chiều cao tầng)'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('3.6. Góc xoay của Family Revit lấy từ đâu?')
    .setChoiceValues([
      'Lấy theo góc xoay của Block CAD',
      'Luôn đặt 0° (không xoay)',
      'Tùy chọn khi chạy tool'
    ]);

  // E. Layer & Filter
  form.addSectionHeaderItem()
    .setTitle('E. Layer & Filter CAD');

  form.addParagraphTextItem()
    .setTitle('3.7. Thiết bị nằm trên layer CAD nào? Liệt kê:')
    .setHelpText('VD: E-LIGHT, E-POWER, M-FIRE-SPK, M-HVAC-DIFF...');

  // F. File mẫu
  form.addSectionHeaderItem()
    .setTitle('F. File mẫu cần cung cấp')
    .setHelpText('Upload lên Google Drive → Share "Anyone with the link" → Dán link bên dưới.');

  form.addCheckboxItem()
    .setTitle('3.8. File mẫu bạn có thể cung cấp:')
    .setChoiceValues([
      'File .dwg (CAD) có block thiết bị',
      'File .rvt mẫu đã link CAD trên',
      'Danh sách block name + Family Revit tương ứng',
      'Family Revit (.rfa) các thiết bị đang dùng',
      'Chưa có, cần chuẩn bị'
    ]);

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File .dwg có block thiết bị (Tool 3)')
    .setHelpText('Dán link Google Drive ở đây.');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Family Revit (.rfa) thiết bị (Tool 3)')
    .setHelpText('Thư mục chứa các Family .rfa đang dùng');

  // ============================================================
  // TOOL 4: ỐNG GIÓ TỪ LINE CAD
  // ============================================================
  form.addPageBreakItem()
    .setTitle('💨 TOOL 4: ỐNG GIÓ TỪ LINE CAD (DUCT FROM CAD)')
    .setHelpText(
      '📖 MÔ TẢ TÍNH NĂNG:\n' +
      'Tool này giúp tự động dựng ống gió (Duct) trong Revit dựa trên đường line trong file CAD link. Mỗi Line Type/Layer CAD được mapping sang Duct Type + Size tương ứng trong Revit.\n\n' +
      '🔴 Hiện tại (thủ công):\n' +
      '• Nhìn bản vẽ CAD link → xác định tuyến ống gió, kích thước từ line type/layer\n' +
      '• Vẽ tay từng đoạn duct trong Revit, chọn đúng Duct Type + Size (W×H)\n' +
      '• Nhập cao độ thủ công cho từng đoạn\n' +
      '• Nối fitting (co, tê, chuyển tiếp) bằng tay → rất chậm khi hệ thống HVAC phức tạp\n\n' +
      '🟢 Sau khi có tool (tự động):\n' +
      '• Chọn layer/line type trong CAD → tool tự nhận diện tuyến ống gió\n' +
      '• Tự dựng duct theo đúng đường đi trong CAD\n' +
      '• Áp đúng Duct Type, Size (W×H hoặc Ø), System Type, cao độ\n' +
      '• Tùy chọn tự nối fitting → tiết kiệm hàng giờ cho mỗi tầng\n\n' +
      '👥 Nhóm liên quan: MEP (HVAC / Thông gió)\n' +
      '⚠️ Nếu bạn KHÔNG làm MEP/HVAC, có thể bỏ qua phần này.'
    );

  form.addMultipleChoiceItem()
    .setTitle('⭐ Mô tả tính năng ở trên có ĐÚNG với nhu cầu thực tế của bạn không?')
    .setChoiceValues([
      'Đúng — mô tả khớp với công việc tôi đang làm',
      'Đúng một phần — cần bổ sung/chỉnh sửa (ghi ở câu tiếp)',
      'Không đúng — tôi hiểu tool này khác (ghi ở câu tiếp)',
      'Không liên quan đến công việc của tôi'
    ]);

  form.addParagraphTextItem()
    .setTitle('↳ Nếu mô tả chưa đúng, bạn mong muốn tool hoạt động như thế nào?')
    .setHelpText('Mô tả chi tiết. VD: "Tool cần hỗ trợ cả ống gió tròn (round duct) chứ không chỉ chữ nhật..."');

  // A. Bảng mapping
  form.addSectionHeaderItem()
    .setTitle('A. Bảng mapping Line Type CAD → Duct Type Revit');

  form.addParagraphTextItem()
    .setTitle('4.1. Liệt kê TẤT CẢ Line Type/Layer CAD và Duct Type Revit tương ứng')
    .setHelpText(
      'Điền theo format (mỗi dòng 1 mapping):\n' +
      'Line Type/Layer CAD → Duct Type Revit | Size (W×H) | System Type\n\n' +
      'VD:\n' +
      'DUCT_SA_400x200 → Rectangular Duct | 400×200 | Supply Air\n' +
      'DUCT_RA_300x200 → Rectangular Duct | 300×200 | Return Air\n' +
      'DUCT_EA_Ø250 → Round Duct | Ø250 | Exhaust Air\n' +
      '...'
    );

  // B. Kích thước & Hình dạng
  form.addSectionHeaderItem()
    .setTitle('B. Kích thước & Hình dạng ống gió');

  form.addCheckboxItem()
    .setTitle('4.2. Hình dạng ống gió cần hỗ trợ:')
    .setChoiceValues([
      'Ống gió chữ nhật (Rectangular Duct)',
      'Ống gió tròn (Round Duct)',
      'Ống gió oval (Oval Duct)',
      'Ống gió mềm (Flex Duct)'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('4.3. Kích thước ống gió xác định thế nào từ CAD?')
    .setChoiceValues([
      'Từ tên Line Type (VD: DUCT_400x200)',
      'Từ tên Layer (VD: M-HVAC-SA-400x200)',
      'Từ text/annotation gần đường line',
      'Nhập thủ công khi chạy tool',
      'Khác'
    ]);

  // C. Cao độ & System
  form.addSectionHeaderItem()
    .setTitle('C. Cao độ & System Type');

  form.addMultipleChoiceItem()
    .setTitle('4.4. Cao độ ống gió xác định thế nào?')
    .setChoiceValues([
      'Nhập 1 cao độ chung cho tất cả duct',
      'Mỗi hệ thống (SA/RA/EA) có cao độ riêng',
      'Lấy từ text/annotation trong CAD'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('4.5. Cao độ tính theo mốc nào?')
    .setChoiceValues([
      'BOD — Bottom of Duct (đáy ống gió)',
      'CL — Center Line (tim ống gió)',
      'TOD — Top of Duct (đỉnh ống gió)'
    ]);

  form.addCheckboxItem()
    .setTitle('4.6. System Type trong Revit:')
    .setChoiceValues([
      'Supply Air (Cấp gió)',
      'Return Air (Hồi gió)',
      'Exhaust Air (Thải gió)',
      'Fresh Air (Gió tươi)',
      'Khác'
    ]);

  // D. Fitting & Layer
  form.addSectionHeaderItem()
    .setTitle('D. Fitting & Layer CAD');

  form.addMultipleChoiceItem()
    .setTitle('4.7. Sau khi dựng duct, có cần tự nối fitting (co, tê, chuyển tiếp) không?')
    .setChoiceValues([
      'Có — tự nối tất cả fitting',
      'Không — chỉ dựng duct thẳng',
      'Tùy chọn khi chạy tool'
    ]);

  form.addParagraphTextItem()
    .setTitle('4.8. Ống gió nằm trên layer CAD nào? Liệt kê:')
    .setHelpText('VD: M-HVAC-SA, M-HVAC-RA, M-HVAC-EA...');

  // E. File mẫu
  form.addSectionHeaderItem()
    .setTitle('E. File mẫu cần cung cấp')
    .setHelpText('Upload lên Google Drive → Share → Dán link bên dưới.');

  form.addCheckboxItem()
    .setTitle('4.9. File mẫu bạn có thể cung cấp:')
    .setChoiceValues([
      'File .dwg (CAD) mẫu có hệ thống ống gió đã vẽ',
      'File .rvt mẫu đã link file CAD trên',
      'Bảng quy ước layer / line type CAD cho HVAC',
      'Chưa có, cần chuẩn bị'
    ]);

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File .dwg (CAD) mẫu ống gió (Tool 4)')
    .setHelpText('Dán link Google Drive ở đây.');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File .rvt đã link CAD (Tool 4)')
    .setHelpText('File Revit đã link file CAD ở trên');

  // ============================================================
  // TOOL 5: THAY ĐỔI ĐỘ DỐC ỐNG
  // ============================================================
  form.addPageBreakItem()
    .setTitle('📐 TOOL 5: B1.28 — THAY ĐỔI ĐỘ DỐC ỐNG')
    .setHelpText(
      '📖 MÔ TẢ TÍNH NĂNG:\n' +
      'Tool này giúp thay đổi độ dốc của ống thoát nước/nước mưa. Chỉ cần nhập giá trị dốc (VD: 1%), ống và các fitting (co, tê) sẽ tự động xoay theo góc tương ứng.\n\n' +
      '🔴 Hiện tại (thủ công):\n' +
      '• Chọn từng đoạn ống → chỉnh Slope thủ công trong Properties\n' +
      '• Các fitting (co, tê) không tự xoay → phải chỉnh tay từng cái\n' +
      '• Khi thay đổi thiết kế (đổi dốc từ 1% sang 2%), phải sửa lại toàn bộ\n' +
      '• Ống thoát nước có nhiều nhánh → rất dễ sai sót\n\n' +
      '🟢 Sau khi có tool (tự động):\n' +
      '• Chọn ống (hoặc cả hệ thống) → nhập độ dốc mới\n' +
      '• Tool tự thay đổi Slope cho tất cả pipe trong hệ thống\n' +
      '• Fitting tự xoay theo góc tương ứng\n' +
      '• Xác định hướng dốc tự động theo flow direction\n\n' +
      '👥 Nhóm liên quan: MEP (Cấp thoát nước)\n' +
      '⚠️ Nếu bạn KHÔNG làm MEP, có thể bỏ qua phần này.'
    );

  form.addMultipleChoiceItem()
    .setTitle('⭐ Mô tả tính năng ở trên có ĐÚNG với nhu cầu thực tế của bạn không?')
    .setChoiceValues([
      'Đúng — mô tả khớp với công việc tôi đang làm',
      'Đúng một phần — cần bổ sung/chỉnh sửa (ghi ở câu tiếp)',
      'Không đúng — tôi hiểu tool này khác (ghi ở câu tiếp)',
      'Không liên quan đến công việc của tôi'
    ]);

  form.addParagraphTextItem()
    .setTitle('↳ Nếu mô tả chưa đúng, bạn mong muốn tool hoạt động như thế nào?')
    .setHelpText('Mô tả chi tiết. VD: "Tool cần hỗ trợ cả thay đổi dốc cho ống cấp nước..."');

  // A. Loại ống áp dụng
  form.addSectionHeaderItem()
    .setTitle('A. Loại ống áp dụng');

  form.addCheckboxItem()
    .setTitle('5.1. Loại ống nào cần thay đổi độ dốc?')
    .setChoiceValues([
      'Ống thoát sàn (Sanitary)',
      'Ống nước mưa (Storm)',
      'Ống PCCC',
      'Tất cả ống nằm ngang',
      'Khác'
    ]);

  // B. Cách nhập & Hướng dốc
  form.addSectionHeaderItem()
    .setTitle('B. Cách nhập & Hướng dốc');

  form.addCheckboxItem()
    .setTitle('5.2. Cách nhập giá trị độ dốc:')
    .setChoiceValues([
      'Phần trăm (VD: 1%, 2%)',
      'Tỷ lệ (VD: 1/100, 1/50)',
      'Góc (VD: 0.57°)',
      'Cả phần trăm và tỷ lệ'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('5.3. Hướng dốc xác định thế nào?')
    .setChoiceValues([
      'Tự xác định theo Flow Direction trong Revit',
      'User chỉ định đầu cao / đầu thấp',
      'Luôn dốc về phía ống đứng / hố ga gần nhất',
      'Khác'
    ]);

  // C. Phạm vi & Fitting
  form.addSectionHeaderItem()
    .setTitle('C. Phạm vi & Fitting');

  form.addMultipleChoiceItem()
    .setTitle('5.4. Phạm vi thay đổi:')
    .setChoiceValues([
      'Chỉ 1 đoạn ống được chọn',
      'Toàn bộ ống nối liền nhau (connected pipe run)',
      'Toàn bộ ống trong 1 system',
      'Tùy chọn khi chạy tool'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('5.5. Khi thay đổi dốc, pipe fitting (co, tê) có tự xoay theo không?')
    .setChoiceValues([
      'Có — fitting phải auto-rotate',
      'Không — chỉ thay dốc ống, fitting giữ nguyên'
    ]);

  // D. Giá trị dốc phổ biến
  form.addSectionHeaderItem()
    .setTitle('D. Giá trị dốc tiêu chuẩn');

  form.addTextItem()
    .setTitle('5.6. Độ dốc tiêu chuẩn ỐNG THOÁT SÀN:')
    .setHelpText('VD: 1%, 2%, 1/100...');

  form.addTextItem()
    .setTitle('5.7. Độ dốc tiêu chuẩn ỐNG THOÁT NGOÀI NHÀ:')
    .setHelpText('VD: 1%, 2%...');

  form.addTextItem()
    .setTitle('5.8. Độ dốc tiêu chuẩn ỐNG NƯỚC MƯA:')
    .setHelpText('VD: 1%, 1.5%...');

  form.addTextItem()
    .setTitle('5.9. Độ dốc loại ống khác (nếu có):')
    .setHelpText('Ghi rõ loại ống và giá trị dốc');

  form.addCheckboxItem()
    .setTitle('5.10. File mẫu bạn có thể cung cấp:')
    .setChoiceValues([
      'File .rvt mẫu có hệ thống ống thoát nước đã dựng',
      'Screenshot thể hiện kết quả mong muốn sau khi thay dốc',
      'Chưa có, cần chuẩn bị'
    ]);

  form.addTextItem()
    .setTitle('📎 Link Google Drive — File .rvt mẫu ống thoát nước (Tool 5)')
    .setHelpText('Dán link Google Drive ở đây.');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Screenshot kết quả mong muốn (Tool 5)')
    .setHelpText('Screenshot hiển thị ống sau khi đã thay đổi độ dốc');

  // ============================================================
  // YÊU CẦU CHUNG
  // ============================================================
  form.addPageBreakItem()
    .setTitle('📎 YÊU CẦU CHUNG & ĐÍNH KÈM TÀI LIỆU')
    .setHelpText(
      'Áp dụng cho tất cả 5 tools.\n\n' +
      '📂 HƯỚNG DẪN GỬI FILE:\n' +
      '1. Upload file lên Google Drive cá nhân\n' +
      '2. Click chuột phải → "Share" → "Anyone with the link" → "Viewer"\n' +
      '3. Copy link → Dán vào ô tương ứng bên dưới\n\n' +
      '💡 Nếu có nhiều file, bạn có thể tạo 1 thư mục Google Drive và chia sẻ link thư mục.'
    );

  form.addMultipleChoiceItem()
    .setTitle('Bạn có template dự án (.rte) sẵn không?')
    .setChoiceValues([
      'Có — sẽ gửi kèm',
      'Không có template riêng',
      'Dùng template mặc định của Revit'
    ]);

  form.addMultipleChoiceItem()
    .setTitle('Bạn có file Shared Parameter (.txt) đang dùng không?')
    .setChoiceValues([
      'Có — sẽ gửi kèm',
      'Không dùng Shared Parameter riêng'
    ]);

  form.addSectionHeaderItem()
    .setTitle('Tài liệu chung')
    .setHelpText('Các file dùng chung cho nhiều tools.');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Template dự án (.rte)')
    .setHelpText('Template Revit đang dùng cho dự án.');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Shared Parameter file (.txt)')
    .setHelpText('File Shared Parameter nếu dự án đang dùng.');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Video quy trình thủ công')
    .setHelpText('Quay lại màn hình Revit khi bạn đang làm thủ công (bất kỳ tool nào). Video giúp developer hiểu rõ workflow thực tế — CỰC KỲ có giá trị!');

  form.addTextItem()
    .setTitle('📎 Link Google Drive — Thư mục tổng hợp tất cả file')
    .setHelpText('Nếu bạn tạo 1 thư mục chứa tất cả file mẫu, dán link thư mục ở đây.');

  form.addParagraphTextItem()
    .setTitle('Ghi chú / Góp ý bổ sung')
    .setHelpText('Bất kỳ thông tin, mong muốn, hoặc lưu ý nào khác cho developer');

  // ============================================================
  // LOG RESULT
  // ============================================================
  Logger.log('✅ Google Form đã được tạo thành công!');
  Logger.log('📝 Tên form: ' + form.getTitle());
  Logger.log('🔗 Link chỉnh sửa: ' + form.getEditUrl());
  Logger.log('🌐 Link gửi cho nhân viên: ' + form.getPublishedUrl());
  Logger.log('📊 Link xem kết quả: ' + form.getSummaryUrl());
}
