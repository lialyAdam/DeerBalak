$(document).on('submit', 'form[action*="TogglePostUseful"]', function (e) {
    e.preventDefault();

    var form = $(this);
    var postId = form.find('input[name="postId"]').val();

    $.ajax({
        type: "POST",
        url: form.attr('action'),
        data: form.serialize(),
        success: function (result) {
            // استبدال البوست بالنسخة الجديدة من السيرفر
            $("#post-" + postId).replaceWith(result);
        },
        error: function () {
            alert("حدث خطأ أثناء الإعجاب بالمنشور.");
        }
    });
});
