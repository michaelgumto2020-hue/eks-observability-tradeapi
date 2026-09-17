resource "aws_sqs_queue" "trade_orders_dlq" {
  name                      = "${var.project_name}-trade-orders-dlq"
  message_retention_seconds = 1209600

  tags = {
    Name = "${var.project_name}-trade-orders-dlq"
  }
}

resource "aws_sqs_queue" "trade_orders" {
  name                       = "${var.project_name}-trade-orders"
  visibility_timeout_seconds = 30
  message_retention_seconds  = 345600

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.trade_orders_dlq.arn
    maxReceiveCount      = 5
  })

  tags = {
    Name = "${var.project_name}-trade-orders"
  }
}

output "sqs_queue_url" {
  value = aws_sqs_queue.trade_orders.url
}