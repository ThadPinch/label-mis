(function () {
    function reindexCollection(listElement, collectionName) {
        var items = listElement.querySelectorAll("[data-collection-item]");
        items.forEach(function (item, index) {
            item.querySelectorAll("[name]").forEach(function (field) {
                field.name = field.name.replace(
                    new RegExp("Input\\." + collectionName + "\\[\\d+\\]"),
                    "Input." + collectionName + "[" + index + "]");
            });

            item.querySelectorAll("[id]").forEach(function (field) {
                field.id = field.id.replace(
                    new RegExp("Input_" + collectionName + "_\\d+__"),
                    "Input_" + collectionName + "_" + index + "__");
            });

            item.querySelectorAll("label[for]").forEach(function (label) {
                label.htmlFor = label.htmlFor.replace(
                    new RegExp("Input_" + collectionName + "_\\d+__"),
                    "Input_" + collectionName + "_" + index + "__");
            });

            item.querySelectorAll("[data-valmsg-for]").forEach(function (span) {
                var valmsgFor = span.getAttribute("data-valmsg-for");
                if (valmsgFor) {
                    span.setAttribute("data-valmsg-for", valmsgFor.replace(
                        new RegExp("Input\\." + collectionName + "\\[\\d+\\]"),
                        "Input." + collectionName + "[" + index + "]"));
                }
            });
        });
    }

    function updateEmptyState(listElement, emptyElementId) {
        var emptyElement = document.getElementById(emptyElementId);
        if (!emptyElement) {
            return;
        }

        var hasItems = listElement.querySelectorAll("[data-collection-item]").length > 0;
        emptyElement.hidden = hasItems;
    }

    function addRow(listId, templateId, emptyElementId, collectionName) {
        var listElement = document.getElementById(listId);
        var template = document.getElementById(templateId);
        if (!listElement || !template) {
            return;
        }

        var index = listElement.querySelectorAll("[data-collection-item]").length;
        var html = template.innerHTML.replaceAll("__INDEX__", index).replaceAll("___INDEX__", "_" + index + "_");
        listElement.insertAdjacentHTML("beforeend", html);
        updateEmptyState(listElement, emptyElementId);
    }

    function initCollection(listId, addButtonId, templateId, emptyElementId, collectionName) {
        var listElement = document.getElementById(listId);
        var addButton = document.getElementById(addButtonId);
        if (!listElement) {
            return;
        }

        listElement.addEventListener("click", function (event) {
            var removeButton = event.target.closest(".remove-item");
            if (!removeButton || !listElement.contains(removeButton)) {
                return;
            }

            var item = removeButton.closest("[data-collection-item]");
            if (!item) {
                return;
            }

            item.remove();
            reindexCollection(listElement, collectionName);
            updateEmptyState(listElement, emptyElementId);
        });

        if (addButton) {
            addButton.addEventListener("click", function () {
                addRow(listId, templateId, emptyElementId, collectionName);
            });
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        initCollection("address-list", "add-address", "address-row-template", "address-empty", "Addresses");
        initCollection("contact-list", "add-contact", "contact-row-template", "contact-empty", "Contacts");
    });
})();
