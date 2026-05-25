window.insertDiscordMarkdown = function (elementId, prefix, suffix) {
    var txtarea = document.getElementById(elementId);
    if (!txtarea) return;
    
    var start = txtarea.selectionStart;
    var finish = txtarea.selectionEnd;
    var sel = txtarea.value.substring(start, finish);
    
    var newText = prefix + sel + suffix;
    
    if (sel.length === 0) {
        txtarea.setRangeText(newText, start, finish, 'end');
        txtarea.selectionStart = start + prefix.length;
        txtarea.selectionEnd = start + prefix.length;
    } else {
        txtarea.setRangeText(newText, start, finish, 'select');
    }
    
    // Dispatch input event to update Blazor binding
    txtarea.dispatchEvent(new Event('input', { bubbles: true }));
    txtarea.focus();
};

window.insertDiscordEmoji = function (elementId, emoji) {
    var txtarea = document.getElementById(elementId);
    if (!txtarea) return;
    
    var start = txtarea.selectionStart;
    var finish = txtarea.selectionEnd;
    
    txtarea.setRangeText(emoji, start, finish, 'end');
    
    // Dispatch input event to update Blazor binding
    txtarea.dispatchEvent(new Event('input', { bubbles: true }));
    txtarea.focus();
};
