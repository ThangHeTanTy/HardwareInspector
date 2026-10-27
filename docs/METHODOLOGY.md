# Quy trình thẩm định máy đã qua sử dụng

Tài liệu này mô tả cách nghĩ đứng sau phần mềm. Đọc nó trước khi đọc code sẽ giúp bạn
hiểu vì sao từng phép kiểm tra tồn tại, và quan trọng hơn — hiểu giới hạn của chúng.

---

## Nguyên tắc nền tảng

**Không tin bất kỳ con số đơn lẻ nào.**

Người bán có thể cài lại Windows, thay pin, lau sạch vỏ.
Nhưng để làm cho *tất cả* các dấu vết cùng khớp với một câu chuyện giả thì cần công sức
vượt xa giá trị của việc lừa một giao dịch. Vì vậy phương pháp ở đây không đi tìm
"con số xấu" — nó đi tìm **chỗ các con số mâu thuẫn với nhau**.

Mỗi linh kiện trong máy mang một loại đồng hồ riêng, và các đồng hồ này độc lập với nhau:

| Nguồn | Đồng hồ nó mang | Xoá được không |
|---|---|---|
| Ổ cứng | Số giờ cấp điện, số lần bật/tắt, tổng dữ liệu đã ghi | Không, nằm trong firmware ổ |
| Pin | Số chu kỳ sạc, dung lượng còn lại, ngày sản xuất | Không, nằm trong vi điều khiển pin |
| Tấm nền | Tuần/năm sản xuất trong EDID | Không, trừ khi thay tấm nền |
| Windows | Ngày cài, nhật ký sự cố | Có, chỉ cần cài lại |
| Vỏ máy | Ốc toét, xước, bản lề | Không, chỉ nhìn được bằng mắt |

Khi bốn đồng hồ đầu tiên kể cùng một câu chuyện, độ tin cậy rất cao.
Khi chúng lệch nhau, điểm lệch chính là câu hỏi cần đặt cho người bán.

---

## Bảy tầng kiểm tra, theo thứ tự ưu tiên

### Tầng 1 — Bo mạch: model gì, khe cắm ra sao, còn nâng cấp được tới đâu

Đọc bảng SMBIOS thô (không qua WMI, vì WMI bỏ bớt dữ liệu) để liệt kê:

- Model bo mạch và hãng máy (máy tự ráp thì dùng tên bo mạch).
- Từng khe RAM: đang cắm thanh gì (dung lượng, loại, tốc độ) hay còn trống; RAM tối đa bo hỗ trợ.
- Từng khe PCIe / M.2: đang dùng hay còn trống, đời PCIe và số làn — từ đó suy ra trần tốc độ SSD.
- Cổng ngoài và đầu cắm trên bo (SATA, quạt, header USB) mà bo mạch tự khai.

Phần này chỉ là thông tin, không trừ điểm: bo ít khe không phải bo hỏng.

**BIOS cố ý không nằm trong tiêu chí.** BIOS đi kèm bo mạch do hãng phát hành và cập nhật.
Trên bo bán lẻ, các trường nó khai — serial, tên hệ thống (`To Be Filled By O.E.M.`,
`Default string`), ngày phát hành, cảm biến mở vỏ, Secure Boot — thường để mặc định ngay từ
khi mới mua, và ngày phát hành BIOS không nói gì về tuổi của chiếc máy cụ thể. Dùng chúng để
chấm điểm chỉ sinh cảnh báo sai trên bo mới. Ngoại lệ duy nhất là mật khẩu BIOS, được báo trong
mục *Khoá & quyền sở hữu* vì nó khoá máy thật sự.

### Tầng 2 — Khoá & quyền sở hữu: máy còn thuộc tổ chức khác không?

Autopilot, MDM, tài khoản tổ chức, Intel AMT, phần mềm chống trộm Absolute và mật khẩu BIOS.
Máy còn bị quản lý có thể bị khoá từ xa sau khi mua.

### Tầng 3 — Ổ lưu trữ: cuốn nhật ký trung thực nhất

S.M.A.R.T. cho biết những thứ mà không ai xoá được bằng cách format:

- **Số giờ cấp điện.** Trên 20.000 giờ là máy dùng cường độ cao;
  trên 35.000 giờ thì bất kể S.M.A.R.T. còn "xanh", rủi ro hỏng đột ngột đã rất lớn.
- **Sector đã tráo / đang chờ tráo / không sửa được.** Bất kỳ con số nào lớn hơn 0
  ở nhóm này đều đáng lo. *Pending sectors* nguy hiểm nhất vì dữ liệu trên đó
  có thể mất bất cứ lúc nào.
- **Tuổi thọ NAND còn lại và tổng TB đã ghi.** Máy đã ghi 200 TB không phải máy văn phòng —
  đó là máy từng chạy máy ảo, dựng phim, hoặc làm node blockchain.
- **Cảm biến va đập (attribute 0xBF).** Con số cao trên ổ cơ nghĩa là máy từng bị rơi.
  Đây là lúc cần soi kỹ bản lề và khung vỏ.
- **Lỗi CRC (0xC7).** Lỗi này nằm ở *cáp và cổng*, không phải ở ổ.
  Phân biệt đúng chỗ giúp bạn không trả tiền oan cho một cái ổ vẫn còn tốt.

### Tầng 4 — Pin: đồng hồ đo tuổi thật của laptop

Số chu kỳ sạc và độ chai pin nằm trong vi điều khiển của chính viên pin.
Một máy có vỏ đẹp như mới nhưng pin đã qua 900 chu kỳ là một máy đã được dùng
rất nhiều, chỉ là được giữ gìn cẩn thận.

Ngược lại — và đây là phép đối chiếu quan trọng — **pin gần như mới trên máy có
ổ cứng chạy 20.000 giờ** nghĩa là pin vừa được thay trước khi bán.
Điều này không xấu, nhưng nó nói với bạn rằng tuổi thật của máy nằm ở con số
giờ chạy ổ cứng, không phải ở tình trạng pin.

### Tầng 5 — Nhiệt: chỉ lộ ra khi máy nóng thật

Đo nhiệt độ lúc máy nghỉ gần như vô nghĩa. Toàn bộ giá trị của bài kiểm tra nhiệt
nằm ở việc chạy tải nặng 15 phút rồi quan sát:

- **Nhiệt độ tối đa.** Chạm ngưỡng cắt nhiệt nghĩa là CPU đang tự hạ xung để tự bảo vệ —
  hiệu năng thực tế thấp hơn thông số đáng kể.
- **Nhiệt độ thấp nhất.** Nhiệt độ *nghỉ* cao là dấu hiệu rõ hơn cả nhiệt độ tải:
  nó nói rằng luồng gió bị chặn hoặc keo tản nhiệt đã mất tác dụng.
- **Chênh lệch nhân ↔ điểm nóng của GPU.** Trên card khoẻ, chênh lệch này dưới 15 °C.
  Trên 25 °C là dấu hiệu kinh điển của keo tản nhiệt đã tách lớp —
  thường gặp ở card từng chạy tải nặng liên tục nhiều tháng.
- **Xung nhịp đạt được so với thiết kế.** Chỉ đạt 70% xung thiết kế nghĩa là
  có thứ gì đó đang giới hạn: nhiệt, công suất, hoặc BIOS.

### Tầng 5b — GPU: để phần mềm tự so từng bit

Card đồ hoạ không có đồng hồ đếm giờ. Cách duy nhất để biết nó còn khoẻ là bắt nó làm việc
nặng rồi kiểm tra kết quả. Bài kiểm tra GPU trong ứng dụng gồm hai phần, chạy nối tiếp:

1. **Tải nặng 5 phút có đối chiếu.** Compute shader chạy chuỗi phép nhân-cộng trên hàng trăm nghìn
   luồng. Kết quả lấy lúc card còn nguội làm chuẩn; sau đó cứ khoảng 250 ms lại tính lại và so
   đến từng bit. Cùng phép tính, cùng dữ liệu thì card khoẻ luôn cho kết quả giống hệt —
   **lệch dù một lần là nhân đồ hoạ không còn ổn định khi nóng.** Hiệu năng phút cuối so với
   phút đầu cho biết card có tự hạ xung không.
2. **Kiểm tra VRAM khi card còn nóng.** Chip nhớ yếu thường chỉ trả sai dữ liệu khi đã nóng,
   nên kiểm lúc nguội sẽ bỏ sót đúng loại lỗi hay gặp nhất ở card từng đào coin.
   Ghi mẫu giả ngẫu nhiên phụ thuộc địa chỉ lên ~80% VRAM riêng, ghi hết rồi mới đọc lại,
   xen kẽ mẫu gốc và mẫu đảo bit để mỗi bit đều được thử ở cả 0 lẫn 1.

Nếu driver sập giữa bài (Windows reset GPU), đó cũng là một kết luận: card khoẻ chạy tải
bao lâu cũng không làm driver sập. Nhật ký sự kiện `Display 4101` trong 90 ngày cho biết
điều này đã từng xảy ra trước khi bạn cầm máy hay chưa.

Trên laptop hai GPU, bài kiểm tra chủ động chọn card rời — nếu để mặc định, Windows sẽ
đưa ứng dụng vào GPU tích hợp và bài kiểm tra chẳng chạm gì tới card đang được quảng cáo.
Nhớ cắm sạc khi chạy, nếu không card bị giới hạn công suất và số liệu sai lệch.

### Tầng 6 — Màn hình: EDID trả lời câu hỏi, mắt trả lời phần còn lại

EDID chứa mã tấm nền thật và tuần/năm sản xuất — đối chiếu với tem máy và
ngày sản xuất pin để biết màn có phải màn gốc không. Việc thay màn tự nó không xấu,
nhưng nó cho biết máy từng bị va đập hoặc hỏng.

EDID còn cho biết màn nhận tín hiệu số hay analog. Điều này quan trọng vì Windows chỉ biết
đầu ra của card đồ hoạ: màn VGA cắm qua bộ chuyển DisplayPort/HDMI→VGA — kể cả cổng VGA trên
bo mạch đời mới, vốn là chip chuyển DP→VGA gắn sẵn — bị Windows báo là DisplayPort.
Công cụ ưu tiên EDID và ghi rõ "VGA — qua bộ chuyển DisplayPort→VGA".

Phần còn lại — điểm chết, điểm sáng, hở sáng, ám màu, lưu ảnh — không có phần mềm nào
phát hiện được. Bắt buộc phải nhìn bằng mắt, trong phòng tối, với các nền đơn sắc.
Đó là lý do công cụ này có sẵn bài kiểm tra toàn màn hình.

### Tầng 7 — Nhật ký sự cố: những gì Windows đã âm thầm ghi lại

Trong 90 ngày gần nhất:

- **WHEA** ghi lỗi ở mức CPU, RAM và bus PCIe. Số lượng lớn thường đi kèm RAM lỗi
  hoặc khe PCIe tiếp xúc kém.
- **Disk event 7 / 51 / 153** báo lỗi truy cập ổ — thường xuất hiện *sớm hơn* cả S.M.A.R.T.
- **Display 4101** là driver đồ hoạ ngừng phản hồi và bị Windows reset (TDR). Lặp lại nhiều lần
  thường đi kèm card quá nhiệt, VRAM lỗi hoặc nguồn cấp cho card yếu.
- **BugCheck** là màn hình xanh. Ba lần trở lên trong 90 ngày là chuyện cần giải thích.

Lưu ý quan trọng: **Windows vừa cài lại thì nhật ký sạch trơn.**
Nếu ngày cài Windows chỉ cách đây vài ngày, đừng coi Event Log sạch là bằng chứng tốt —
hãy dựa hoàn toàn vào số liệu phần cứng vốn không bị xoá.

---

## Cách đọc kết quả

Công cụ chia phát hiện theo **mức độ chắc chắn**, không chỉ theo mức nghiêm trọng:

- **Bằng chứng cứng** (độ tin cậy ≥ 0.9): S.M.A.R.T. báo sector hỏng, pin chai 45%,
  CPUID không khớp tên CPU. Những thứ này đọc trực tiếp từ phần cứng, không suy diễn.
- **Suy đoán có cơ sở** (0.5 – 0.9): hiệu năng GPU thấp hơn thông số model,
  pin mới trên máy có ổ chạy rất lâu. Cần đối chiếu thêm trước khi kết luận.
- **Tín hiệu yếu** (< 0.5): dùng để gợi ý hướng kiểm tra, không dùng để kết tội.

Điểm trừ được nhân với độ tin cậy, nên một suy đoán yếu không kéo tụt điểm
như một bằng chứng cứng.

**Hãy chạy với quyền Administrator.** Không có quyền đó, phần mềm mất S.M.A.R.T.,
cảm biến nhiệt và một phần kiểm tra khoá doanh nghiệp.

---

## Những gì công cụ này *không* làm được

Trung thực về giới hạn quan trọng ngang với việc phát hiện vấn đề:

1. **Không phát hiện được RAM lỗi nhẹ.** Phải chạy MemTest86 từ USB, ngoài Windows.
2. **Không nhìn thấy màn hình.** Bài kiểm tra GPU tự bắt được lỗi VRAM và lỗi tính toán,
   nhưng lỗi chỉ xuất hiện ở khâu dựng hình 3D hoặc đường xuất tín hiệu (sọc, nhấp nháy,
   mất hình qua một cổng) vẫn phải chạy tải đồ hoạ thật và nhìn bằng mắt. Ngoài ra, khi Windows
   thiếu VRAM nó có thể lặng lẽ đẩy dữ liệu sang RAM hệ thống; phần mềm đã chừa khoảng trống
   theo ngân sách VRAM mà Windows báo, nhưng không thể bảo đảm 100% từng byte đã kiểm
   nằm trên chip nhớ của card.
3. **Không đánh giá được tình trạng vật lý.** Tụ phồng, bản lề nứt, ốc toét, dấu ẩm mốc —
   tất cả đều phải mở máy ra nhìn.
4. **Không kiểm tra được nguồn (PSU).** Với PC bàn, đây thường là linh kiện nguy hiểm nhất
   và cũng là thứ người bán ít nói tới nhất.

Vì vậy mọi báo cáo đều kết thúc bằng một checklist kiểm tra tay, và checklist đó
thay đổi theo những gì vừa phát hiện được trên máy.
